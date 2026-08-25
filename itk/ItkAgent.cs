using A2A;
using A2A.Itk.Proto;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace A2A.Itk;

/// <summary>
/// ITK instruction-handling agent. Parses nested traversal instructions,
/// resolves agent cards, forwards messages, and collects traces.
/// </summary>
public sealed class ItkAgent(IHttpClientFactory httpClientFactory, ILogger<ItkAgent> logger) : IAgentHandler
{
    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
        await updater.SubmitAsync(cancellationToken: cancellationToken);
        await updater.StartWorkAsync(cancellationToken: cancellationToken);

        var instruction = ExtractInstruction(context.Message);
        if (instruction is null)
        {
            logger.LogError("No valid instruction found in request");
            await updater.FailAsync(
                new Message { Role = Role.Agent, MessageId = Guid.NewGuid().ToString("N"), Parts = [Part.FromText("No valid instruction found in request")] },
                cancellationToken: cancellationToken);
            return;
        }

        bool shouldHold = ShouldHoldTask(instruction);

        try
        {
            var results = await HandleInstructionAsync(instruction, cancellationToken);
            var responseText = string.Join("\n", results);
            logger.LogInformation("Response: {Response}", responseText);

            if (shouldHold)
            {
                logger.LogInformation("Holding task {TaskId} as requested", context.TaskId);
                // Emit response + task-finished marker, then keep emitting status updates
                await updater.StartWorkAsync(
                    new Message
                    {
                        Role = Role.Agent,
                        MessageId = Guid.NewGuid().ToString("N"),
                        Parts = [Part.FromText(responseText + "\ntask-finished")]
                    },
                    cancellationToken: cancellationToken);

                await Task.Delay(2000, cancellationToken);

                // Keep emitting periodic status updates until cancelled
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        await updater.StartWorkAsync(cancellationToken: cancellationToken);
                        await Task.Delay(2000, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    logger.LogInformation("Task {TaskId} cancelled", context.TaskId);
                }
            }
            else
            {
                await updater.CompleteAsync(
                    new Message
                    {
                        Role = Role.Agent,
                        MessageId = Guid.NewGuid().ToString("N"),
                        Parts = [Part.FromText(responseText)]
                    },
                    cancellationToken: cancellationToken);
                logger.LogInformation("Task {TaskId} completed", context.TaskId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during instruction handling");
            await updater.FailAsync(cancellationToken: cancellationToken);
        }
    }

    public async Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        logger.LogInformation("Cancel requested for task {TaskId}", context.TaskId);
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
        await updater.CancelAsync(cancellationToken: cancellationToken);
    }

    private Instruction? ExtractInstruction(Message message)
    {
        foreach (var part in message.Parts)
        {
            // Binary protobuf part
            if (part.Raw is not null &&
                (part.MediaType == "application/x-protobuf" || part.Filename == "instruction.bin"))
            {
                try
                {
                    var inst = new Instruction();
                    inst.MergeFrom(new CodedInputStream(part.Raw));
                    return inst;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to parse instruction from binary part");
                }
            }

            // Base64-encoded instruction in text part
            if (part.Text is not null)
            {
                try
                {
                    var raw = Convert.FromBase64String(part.Text);
                    var inst = new Instruction();
                    inst.MergeFrom(new CodedInputStream(raw));
                    return inst;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to parse instruction from text part");
                }
            }
        }

        return null;
    }

    private static bool ShouldHoldTask(Instruction inst)
    {
        if (inst.ReturnResponse is { HoldTask: true })
            return true;
        if (inst.Steps is not null)
            return inst.Steps.Instructions.Any(ShouldHoldTask);
        return false;
    }

    private async Task<List<string>> HandleInstructionAsync(Instruction inst, CancellationToken cancellationToken)
    {
        if (inst.CallAgent is not null)
            return await HandleCallAgentAsync(inst.CallAgent, cancellationToken);

        if (inst.ReturnResponse is not null)
            return [inst.ReturnResponse.Response];

        if (inst.Steps is not null)
        {
            var results = new List<string>();
            foreach (var step in inst.Steps.Instructions)
            {
                var stepResults = await HandleInstructionAsync(step, cancellationToken);
                results.AddRange(stepResults);
            }
            return results;
        }

        throw new InvalidOperationException("Unknown instruction type");
    }

    private async Task<List<string>> HandleCallAgentAsync(CallAgent call, CancellationToken cancellationToken)
    {
        logger.LogInformation("Calling agent {AgentCardUri} via {Transport}", call.AgentCardUri, call.Transport);

        var httpClient = httpClientFactory.CreateClient();

        // Select protocol binding based on transport
        var preferredBinding = call.Transport.ToUpperInvariant() switch
        {
            "JSONRPC" => ProtocolBindingNames.JsonRpc,
            "HTTP+JSON" or "HTTP_JSON" or "REST" => ProtocolBindingNames.HttpJson,
            _ => throw new NotSupportedException($"Unsupported transport: {call.Transport}")
        };

        AgentCard agentCard;
        try
        {
            // Try to resolve agent card from the URI to discover transport-specific endpoints
            var baseUri = call.AgentCardUri.EndsWith('/') ? call.AgentCardUri : call.AgentCardUri + "/";
            var cardResolver = new A2ACardResolver(new Uri(baseUri), httpClient);
            agentCard = await cardResolver.GetAgentCardAsync(cancellationToken);
            logger.LogInformation("Resolved agent card with {Count} interfaces", agentCard.SupportedInterfaces?.Count ?? 0);
        }
        catch (Exception ex)
        {
            // Fall back to constructing a minimal card from instruction data
            logger.LogWarning(ex, "Failed to resolve agent card, using constructed card");
            agentCard = new AgentCard
            {
                Name = "remote",
                Description = "remote",
                Version = "1.0",
                Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = true },
                DefaultInputModes = ["text/plain"],
                DefaultOutputModes = ["text/plain"],
                SupportedInterfaces =
                [
                    new AgentInterface
                    {
                        ProtocolBinding = preferredBinding,
                        Url = call.AgentCardUri,
                        ProtocolVersion = "1.0",
                    }
                ],
            };
        }

        var clientOptions = new A2AClientOptions
        {
            PreferredBindings = [preferredBinding]
        };

        var client = A2AClientFactory.Create(agentCard, httpClient, clientOptions);

        // Wrap nested instruction into a message
        var nestedMessage = WrapInstructionToMessage(call.Instruction!);
        var request = new SendMessageRequest { Message = nestedMessage };

        // Configure push notification if specified
        if (call.PushNotification is not null)
        {
            var url = call.PushNotification.Url;
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = $"http://{url}";

            request.Configuration = new SendMessageConfiguration
            {
                PushNotificationConfig = new PushNotificationConfig
                {
                    Url = $"{url}/notifications",
                    Token = "itk-token"
                }
            };
        }

        var results = new List<string>();

        if (call.Resubscribe is not null)
        {
            results.AddRange(await HandleResubscribeAsync(client, request, cancellationToken));
        }
        else if (call.Streaming)
        {
            // Use streaming only when explicitly requested
            await foreach (var response in client.SendStreamingMessageAsync(request, cancellationToken))
            {
                logger.LogInformation("Stream event: {Response}", response);
                results.AddRange(ExtractTextFromStreamResponse(response));
            }
        }
        else
        {
            // Non-streaming send (works for both JSON-RPC and HTTP+JSON)
            var response = await client.SendMessageAsync(request, cancellationToken);
            logger.LogInformation("SendMessage response task state: {State}", response.Task?.Status?.State);
            results.AddRange(ExtractTextFromSendMessageResponse(response));
        }

        return results;
    }

    private async Task<List<string>> HandleResubscribeAsync(IA2AClient client, SendMessageRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Executing re-subscribe behavior");
        var results = new List<string>();
        string? taskId = null;

        // Send initial message and disconnect after first event
        await foreach (var response in client.SendStreamingMessageAsync(request, cancellationToken))
        {
            taskId = ExtractTaskId(response);
            if (taskId is not null) break;
        }

        if (taskId is null)
        {
            throw new InvalidOperationException("No task ID received from initial stream");
        }

        logger.LogInformation("Disconnected from task {TaskId}. Now re-subscribing.", taskId);

        // Re-subscribe to the task
        var subscribeRequest = new SubscribeToTaskRequest { Id = taskId };
        bool finished = false;

        await foreach (var response in client.SubscribeToTaskAsync(subscribeRequest, cancellationToken))
        {
            logger.LogInformation("Event after re-subscribe: {Response}", response);

            var texts = ExtractTextFromStreamResponse(response);
            foreach (var text in texts)
            {
                var processed = text.Replace("task-finished", "");
                results.Add(processed);
            }

            if (texts.Any(t => t.Contains("task-finished")))
            {
                logger.LogInformation("Received task-finished after re-subscribe");
                finished = true;
                break;
            }

            // Also check task history
            if (response.Task is not null)
            {
                foreach (var msg in response.Task.History ?? [])
                {
                    if (msg.Role == Role.Agent)
                    {
                        foreach (var part in msg.Parts)
                        {
                            if (part.Text is not null && part.Text.Contains("task-finished"))
                            {
                                results.Add(part.Text.Replace("task-finished", ""));
                                finished = true;
                                break;
                            }
                        }
                    }
                    if (finished) break;
                }
            }

            if (finished) break;
        }

        // Cancel if not finished naturally
        if (!finished)
        {
            logger.LogInformation("Canceling task {TaskId} after retrieval", taskId);
            try
            {
                await client.CancelTaskAsync(new CancelTaskRequest { Id = taskId }, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to cancel task {TaskId}", taskId);
                throw;
            }
        }

        return results;
    }

    private static Message WrapInstructionToMessage(Instruction instruction)
    {
        using var ms = new MemoryStream();
        using var cos = new CodedOutputStream(ms);
        instruction.WriteTo(cos);
        cos.Flush();
        var bytes = ms.ToArray();

        return new Message
        {
            Role = Role.User,
            MessageId = Guid.NewGuid().ToString("N"),
            Parts =
            [
                new Part
                {
                    Raw = bytes,
                    MediaType = "application/x-protobuf",
                    Filename = "instruction.bin"
                }
            ]
        };
    }

    private static string? ExtractTaskId(StreamResponse response)
    {
        if (response.Task is not null)
            return response.Task.Id;
        if (response.StatusUpdate is not null)
            return response.StatusUpdate.TaskId;
        return null;
    }

    private static List<string> ExtractTextFromStreamResponse(StreamResponse response)
    {
        var results = new List<string>();

        Message? message = response.Message
            ?? response.Task?.Status?.Message;
        if (message is null && response.StatusUpdate?.Status?.Message is not null)
            message = response.StatusUpdate.Status.Message;

        if (message is not null)
        {
            foreach (var part in message.Parts)
            {
                if (part.Text is not null)
                    results.Add(part.Text);
            }
        }

        return results;
    }

    private static List<string> ExtractTextFromSendMessageResponse(SendMessageResponse response)
    {
        var results = new List<string>();

        Message? message = response.Message
            ?? response.Task?.Status?.Message;
        if (message is not null)
        {
            foreach (var part in message.Parts)
            {
                if (part.Text is not null)
                    results.Add(part.Text);
            }
        }

        // Also check history
        if (response.Task?.History is not null)
        {
            foreach (var msg in response.Task.History)
            {
                if (msg.Role == Role.Agent)
                {
                    foreach (var part in msg.Parts)
                    {
                        if (part.Text is not null)
                            results.Add(part.Text);
                    }
                }
            }
        }

        return results;
    }

    public static AgentCard GetAgentCard(int httpPort) => new()
    {
        Name = "ITK .NET Agent",
        Description = ".NET agent for ITK compatibility testing.",
        Version = "1.0.0",
        Capabilities = new AgentCapabilities
        {
            Streaming = true,
            PushNotifications = false,
        },
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
        SupportedInterfaces =
        [
            new AgentInterface
            {
                ProtocolBinding = "JSONRPC",
                Url = $"http://127.0.0.1:{httpPort}/jsonrpc",
                ProtocolVersion = "1.0",
            },
            new AgentInterface
            {
                ProtocolBinding = "HTTP+JSON",
                Url = $"http://127.0.0.1:{httpPort}/",
                ProtocolVersion = "1.0",
            },
        ],
    };
}

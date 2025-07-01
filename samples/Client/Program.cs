// Warning: This file was largely LLM generated. YMMV
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using A2A.Core;


namespace Client;

class Program
{
    private const string DefaultAgentUrl = "http://localhost:5048/echotasks";
    private const string DefaultAgentName = "Echo Agent";
    private static string agentUrl = DefaultAgentUrl;
    private static string agentName = DefaultAgentName;
    private static bool useStreamingMode = false;
    private static HttpClient? httpClient;
    private static A2AClient? client;
    private static string currentSessionId = Guid.NewGuid().ToString("N");
    // The version could also come from assembly info or a version file
    private static readonly string ServiceVersion = "1.0.0";
    private static readonly string ServiceName = "A2A.Client";
    private static readonly ActivitySource activitySource = new ActivitySource(ServiceName, ServiceVersion);
    private static void ParseCommandLineArgs(string[] args)
    {
        if (args.Length == 0)
        {
            return;
        }

        // Check for help argument first
        foreach (var arg in args)
        {
            if (arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--help", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Usage: Client.exe [options] [agentUrl] [agentName]");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  -s, --stream  Enable streaming mode for responses");
                Console.WriteLine("  -h, --help    Display this help message");
                Console.WriteLine();
                Console.WriteLine("Arguments:");
                Console.WriteLine("  agentUrl   - URL of the agent endpoint (default: http://localhost:5048/echo)");
                Console.WriteLine("  agentName  - Display name of the agent (default: Echo Agent)");
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine("  Client.exe                                       # Use default Echo Agent");
                Console.WriteLine("  Client.exe http://localhost:5048/researcher      # Use Researcher Agent");
                Console.WriteLine("  Client.exe http://localhost:5048/travel \"Travel Agent\"  # Use Travel Agent with custom name");
                Console.WriteLine("  Client.exe -s http://localhost:5048/echo         # Use Echo Agent with streaming mode");
                Environment.Exit(0);
            }
        }

        // Check for streaming flag
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("-s", StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals("--stream", StringComparison.OrdinalIgnoreCase))
            {
                useStreamingMode = true;
                
                // Shift remaining args left if this was the first argument
                if (i == 0 && args.Length > 1)
                {
                    Array.Copy(args, 1, args, 0, args.Length - 1);
                    Array.Resize(ref args, args.Length - 1);
                }
                break;
            }
        }

        if (args.Length >= 1 && !args[0].StartsWith("-"))
        {
            agentUrl = args[0];
        }

        if (args.Length >= 2)
        {
            agentName = args[1];
        }
    }
    static async Task Main(string[] args)
    {
        ParseCommandLineArgs(args);

        var builder = Host.CreateApplicationBuilder(args);

        // Configure OpenTelemetry
        InitializeOpenTelemetry(builder);

        var host = builder.Build();
        await host.StartAsync();

        // Use standard HttpClient with instrumentation already provided by OpenTelemetry
        httpClient = new HttpClient
        {
            BaseAddress = new Uri(agentUrl)
        };
        client = new A2AClient(httpClient);
        Console.WriteLine("========================================");
        Console.WriteLine($"{agentName} Chat - A2A Client Experience");
        Console.WriteLine("========================================");
        Console.WriteLine($"Connected to: {agentUrl}");
        Console.WriteLine($"Mode: {(useStreamingMode ? "Streaming" : "Standard")}");
        Console.WriteLine("Type your message and press Enter to send");
        Console.WriteLine("Type 'exit' to quit");
        Console.WriteLine("========================================");
        Console.WriteLine("OpenTelemetry tracing enabled with OTLP exporter");
        Console.WriteLine($"Export endpoint: {Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317"}");
        Console.WriteLine(); while (true)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("You: ");
            Console.ResetColor();

            string? userInput = Console.ReadLine();

            if (string.IsNullOrEmpty(userInput))
                continue;

            if (userInput.ToLower() == "exit")
                break;

            try
            {
                // Choose method based on streaming mode
                A2AResponse response;
                if (useStreamingMode)
                {
                    response = await SendMessageToAgentStreaming(userInput);
                }
                else
                {
                    response = await SendMessageToAgent(userInput);
                }
                if (response is AgentTask agentTask)
                {
                    DisplayAgentResponse(agentTask);
                }
                else if (response is Message message)
                {
                    Console.WriteLine($"{agentName}: {string.Join(" ", message.Parts.OfType<TextPart>().Select(p => p.Text))}");
                }
                else
                {
                    Console.WriteLine($"{agentName} returned an unexpected response type.");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
            }
        }

        await host.StopAsync();
    }
    private static void InitializeOpenTelemetry(IHostApplicationBuilder builder)
    {
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(c => c.AddService(ServiceName))
            .WithTracing(tracing =>
            {
                tracing.AddHttpClientInstrumentation();
                tracing.AddSource(activitySource.Name);
                tracing.AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri("http://localhost:4317");
                    options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                });
            });
    }

    private static async Task<AgentTask> SendMessageToAgentStreaming(string messageText)
    {
        if (client == null)
        {
            throw new InvalidOperationException("A2A client is not initialized");
        }

        // Create a TaskSendParams with the user's message
        var taskSendParams = new MessageSendParams
        {
            Message = new Message
            {
                Role = MessageRole.User,
                Parts = new List<Part>
                {
                    new TextPart
                    {
                        Text = messageText
                    }
                }
            }
        };

        string? taskId = null;
        await foreach (var item in client.SendMessageStreamAsync(taskSendParams))
        {
            switch (item.Data)
            {
                case TaskStatusUpdateEvent taskUpdateEvent:
                    taskId = taskUpdateEvent.TaskId;
                    Console.WriteLine($"Task {taskUpdateEvent.TaskId} updated: {taskUpdateEvent.Status.State}");
                    break;
                case TaskArtifactUpdateEvent taskArtifactEvent:
                    taskId = taskArtifactEvent.TaskId;
                    Console.WriteLine($"Task {taskArtifactEvent.TaskId} artifact updated: {taskArtifactEvent.Artifact.Name}");
                    break;
                case AgentTask agentTask:
                    taskId = agentTask.Id;
                    Console.WriteLine($"Received task {agentTask.Id} with status {agentTask.Status.State}");
                    if (agentTask.Artifacts != null && agentTask.Artifacts.Count > 0)
                    {
                        Console.WriteLine($"Artifacts count: {agentTask.Artifacts.Count}");
                    }
                    break;
                default:
                    Console.WriteLine($"Unknown event type: {item.EventType}");
                    break;
            }
        }

        if (string.IsNullOrEmpty(taskId))
        {
            throw new InvalidOperationException("No task ID received from the agent");
        }
        var result = await client.GetTaskAsync(taskId);
        return result;
    }

    private static async Task<A2AResponse> SendMessageToAgent(string messageText)
    {
        if (client == null)
        {
            throw new InvalidOperationException("A2A client is not initialized");
        }

        // Create an activity for this operation with more detailed options
        using var activity = activitySource.StartActivity(
            "SendAgentMessage",
            ActivityKind.Client,
            new ActivityContext(),
            new Dictionary<string, object?>
            {
                ["agent.url"] = agentUrl,
                ["agent.name"] = agentName,
                ["session.id"] = currentSessionId,
                ["message.length"] = messageText.Length
            });

        Console.WriteLine($"Debug: Started activity {activity?.Id} of kind {activity?.Kind}");

        // Create a TaskSendParams with the user's message
        var taskSendParams = new MessageSendParams
        {
            Message = new Message
            {
                ContextId = currentSessionId,
                MessageId = Guid.NewGuid().ToString("N"),
                Role = MessageRole.User,
                Parts = new List<Part>
                {
                    new TextPart
                    {
                        Text = messageText
                    }
                }
            }
        };

        try
        {
            activity?.AddEvent(new ActivityEvent("SendingMessage"));
            // Send the message using the A2A client
            var result = await client.SendMessageAsync(taskSendParams);

            if (result is AgentTask agentTask)
            {
                activity?.SetTag("task.id", agentTask.Id);

                // Wait for the agent to complete processing
                while (agentTask.Status?.State != TaskState.Completed &&
                       agentTask.Status?.State != TaskState.Failed)
                {
                    // Poll for task updates
                    activity?.AddEvent(new ActivityEvent("PollingForUpdate"));
                    await Task.Delay(200);
                    result = await client.GetTaskAsync(agentTask.Id);
                }

                activity?.SetTag("task.status", agentTask.Status?.State.ToString());
                activity?.AddEvent(new ActivityEvent("ReceivedResponse"));
                return result;
            } 
            else if (result is Message message)
            {
                return message;
            }
            else
            {
                throw new InvalidOperationException("Unexpected response type from agent");
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
    }
    private static void DisplayAgentResponse(AgentTask task)
    {
        // Create an activity for displaying the response
        using var activity = activitySource.StartActivity("DisplayAgentResponse");
        activity?.SetTag("task.id", task.Id);
        activity?.SetTag("task.status", task.Status?.State.ToString());
        activity?.SetTag("task.hasArtifacts", task.Artifacts != null && task.Artifacts.Count > 0);

        if (task.Artifacts != null && task.Artifacts.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"{agentName}: ");
            Console.ResetColor();

            int artifactCount = 0;
            foreach (var artifact in task.Artifacts)
            {
                artifactCount++;
                activity?.SetTag($"artifact.{artifactCount}.name", artifact.Name ?? "unnamed");
                activity?.SetTag($"artifact.{artifactCount}.partCount", artifact.Parts.Count);

                foreach (var part in artifact.Parts)
                {
                    if (part is TextPart textPart)
                    {
                        Console.WriteLine(textPart.Text);
                    }
                    else
                    {
                        Console.WriteLine("[Non-text content]");
                    }
                }
            }
            Console.WriteLine();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{agentName} returned no response.");
            Console.ResetColor();
        }
    }
}

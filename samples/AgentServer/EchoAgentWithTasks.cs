using A2A;
using System.Text.Json;

namespace AgentServer;

public sealed class EchoAgentWithTasks : IAgentHandler
{
    public async Task ExecuteAsync(AgentContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var targetState = GetTargetStateFromMetadata(context.Message.Metadata);
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);

        await updater.SubmitAsync(cancellationToken);
        await updater.AddArtifactAsync(
            [Part.FromText($"Echo: {context.UserText}")], cancellationToken: cancellationToken);

        // Transition to the target state (defaults to Completed)
        switch (targetState)
        {
            case TaskState.Failed:
                await updater.FailAsync(cancellationToken: cancellationToken);
                break;
            case TaskState.Canceled:
                await updater.CancelAsync(cancellationToken);
                break;
            case TaskState.InputRequired:
                await updater.RequireInputAsync(
                    new Message { Role = Role.Agent, MessageId = Guid.NewGuid().ToString("N"), Parts = [Part.FromText("Need input")] },
                    cancellationToken);
                break;
            default:
                await updater.CompleteAsync(cancellationToken: cancellationToken);
                break;
        }
    }

    public static AgentCard GetAgentCard(string agentUrl) =>
        new()
        {
            Name = "Echo Agent",
            Description = "Agent which will echo every message it receives.",
            Version = "1.0.0",
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    Url = agentUrl,
                    ProtocolBinding = "JSONRPC",
                    ProtocolVersion = "1.0",
                }
            ],
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["text/plain"],
            Capabilities = new AgentCapabilities
            {
                Streaming = true,
                PushNotifications = false,
            },
            Skills =
            [
                new AgentSkill
                {
                    Id = "echo",
                    Name = "Echo",
                    Description = "Echoes back the user message with task tracking.",
                    Tags = ["echo", "test"],
                }
            ],
        };

    private static TaskState? GetTargetStateFromMetadata(Dictionary<string, JsonElement>? metadata)
    {
        if (metadata?.TryGetValue("task-target-state", out var targetStateElement) == true)
        {
            if (Enum.TryParse<TaskState>(targetStateElement.GetString(), true, out var state))
            {
                return state;
            }
        }

        return null;
    }
}
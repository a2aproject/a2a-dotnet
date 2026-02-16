using A2A;
using System.Text.Json;

using TaskStatus = A2A.TaskStatus;

namespace AgentServer;

public class EchoAgentWithTasks
{
    private ITaskStore? _store;

    public void Attach(TaskManager taskManager, ITaskStore store)
    {
        _store = store;
        taskManager.OnSendMessage = OnSendMessageAsync;
    }

    private async Task<SendMessageResponse> OnSendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageText = request.Message.Parts.FirstOrDefault(p => p.Text is not null)?.Text ?? string.Empty;
        var targetState = GetTargetStateFromMetadata(request.Message.Metadata) ?? TaskState.Completed;

        var taskId = Guid.NewGuid().ToString("N");
        var contextId = request.Message.ContextId ?? Guid.NewGuid().ToString("N");

        var task = new AgentTask
        {
            Id = taskId,
            ContextId = contextId,
            Status = new TaskStatus
            {
                State = targetState,
                Timestamp = DateTimeOffset.UtcNow,
            },
            History = [request.Message],
            Artifacts =
            [
                new Artifact
                {
                    ArtifactId = Guid.NewGuid().ToString("N"),
                    Parts = [Part.FromText($"Echo: {messageText}")],
                }
            ],
        };

        await _store!.SetTaskAsync(task, cancellationToken);
        return new SendMessageResponse { Task = task };
    }

    public AgentCard GetAgentCard(string agentUrl)
    {
        return new AgentCard
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
    }

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
using A2A;
using System.Text.Json;

namespace AgentServer;

public class EchoAgentWithTasks
{
    private ITaskManager? _taskManager;

    public void Attach(ITaskManager taskManager)
    {
        _taskManager = taskManager;
        taskManager.OnTaskCreated = ProcessMessageAsync;
        taskManager.OnTaskUpdated = ProcessMessageAsync;
        taskManager.OnAgentCardQuery = GetAgentCardAsync;
    }

    private async Task ProcessMessageAsync(AgentTask task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Process the message
        var lastMessage = task.History!.Last();
        var messageText = lastMessage.Parts.OfType<TextPart>().First().Text;

        // Check for target-state metadata to determine task behavior
        TaskState targetState = GetTargetStateFromMetadata(lastMessage.Metadata) ?? TaskState.Completed;

        // This is a short-lived task - complete it immediately
        await _taskManager!.ReturnArtifactAsync(task.Id, new Artifact()
        {
            Parts = [new TextPart() {
                    Text = $"Echo: {messageText}"
                }]
        }, cancellationToken);

        await _taskManager!.UpdateStatusAsync(
            task.Id,
            status: targetState,
            final: targetState is TaskState.Completed or TaskState.Canceled or TaskState.Failed or TaskState.Rejected,
            cancellationToken: cancellationToken);
    }

    private Task<AgentCard> GetAgentCardAsync(string agentUrl, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AgentCard>(cancellationToken);
        }

        var capabilities = new AgentCapabilities()
        {
            Streaming = true,
            PushNotifications = false,
        };

        return Task.FromResult(new AgentCard()
        {
            Name = "Echo Agent",
            Description = "Agent which will echo every message it receives.",
            Url = agentUrl,
            Version = "1.0.0",
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Capabilities = capabilities,
            Skills = [],
        });
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
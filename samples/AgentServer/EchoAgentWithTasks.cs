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

        // Demonstrate different artifact update patterns based on message content
        if (messageText.StartsWith("stream:", StringComparison.OrdinalIgnoreCase))
        {
            // Demonstrate streaming with UpdateArtifactAsync by sending chunks
            var content = messageText.Substring(7).Trim(); // Remove "stream:" prefix
            var chunks = content.Split(' ');

            for (int i = 0; i < chunks.Length; i++)
            {
                bool isLastChunk = i == chunks.Length - 1;
                await _taskManager!.UpdateArtifactAsync(task.Id, new Artifact()
                {
                    Parts = [new TextPart() { Text = $"Echo chunk {i + 1}: {chunks[i]}" }]
                }, append: i > 0, lastChunk: isLastChunk, cancellationToken);
            }
        }
        else if (messageText.StartsWith("append:", StringComparison.OrdinalIgnoreCase))
        {
            // Demonstrate appending to existing artifacts
            var content = messageText.Substring(7).Trim(); // Remove "append:" prefix

            // First, create an initial artifact (append=false for new artifact)
            await _taskManager!.UpdateArtifactAsync(task.Id, new Artifact()
            {
                Parts = [new TextPart() { Text = $"Initial echo: {content}" }]
            }, append: false, cancellationToken: cancellationToken);

            // Then append additional content (append=true to add to existing)
            await _taskManager!.UpdateArtifactAsync(task.Id, new Artifact()
            {
                Parts = [new TextPart() { Text = $" | Appended: {content.ToUpper(System.Globalization.CultureInfo.InvariantCulture)}" }]
            }, append: true, lastChunk: true, cancellationToken);
        }
        else
        {
            // Default behavior: use ReturnArtifactAsync for simple, complete responses
            await _taskManager!.ReturnArtifactAsync(task.Id, new Artifact()
            {
                Parts = [new TextPart() {
                    Text = $"Echo: {messageText}"
                }]
            }, cancellationToken);
        }

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
            Description = "Agent which will echo every message it receives. Supports special commands: 'stream: <text>' for chunked responses, 'append: <text>' for appending to artifacts, or regular text for simple echo.",
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
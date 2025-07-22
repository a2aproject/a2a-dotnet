using A2A;

namespace AgentServer;

public class EchoAgentWithTasks
{
    private ITaskManager? _taskManager;

    public void Attach(ITaskManager taskManager)
    {
        _taskManager = taskManager;
        taskManager.OnTaskCreated = ProcessMessageAsync;
        taskManager.OnTaskUpdated = ProcessMessageAsync;
        taskManager.OnAgentCardQuery = GetAgentCard;
    }

    private async Task ProcessMessageAsync(AgentTask task, CancellationToken cancellationToken)
    {
        // Process the message
        var messageText = task.History!.Last().Parts.OfType<TextPart>().First().Text;

        await _taskManager!.ReturnArtifactAsync(task.Id, new Artifact()
        {
            Parts = [new TextPart() {
                Text = $"Echo: {messageText}"
            }]
        }, cancellationToken);
        await _taskManager!.UpdateStatusAsync(task.Id, TaskState.Completed, final: true, cancellationToken: cancellationToken);
    }

    private AgentCard GetAgentCard(string agentUrl, CancellationToken _)
    {
        var capabilities = new AgentCapabilities()
        {
            Streaming = true,
            PushNotifications = false,
        };

        return new AgentCard()
        {
            Name = "Echo Agent",
            Description = "Agent which will echo every message it receives.",
            Url = agentUrl,
            Version = "1.0.0",
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Capabilities = capabilities,
            Skills = [],
        };
    }
}
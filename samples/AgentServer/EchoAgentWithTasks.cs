using A2A;

namespace AgentServer;

public class EchoAgentWithTasks
{
    private TaskManager? _taskManager;

    public void Attach(TaskManager taskManager)
    {
        _taskManager = taskManager;
        taskManager.OnTaskCreated = ProcessMessage;
        taskManager.OnTaskUpdated = ProcessMessage;
        taskManager.OnAgentCardQuery = GetAgentCard;
    }

    public async Task ProcessMessage(AgentTask task)
    {
        // Process the message
        var messageText = task.History!.Last().Parts.OfType<TextPart>().First().Text;

        await _taskManager!.ReturnArtifactAsync(task.Id, new Artifact()
        {
            Parts = [new TextPart() {
                Text = $"Echo: {messageText}"
            }]
        });
        await _taskManager!.UpdateStatusAsync(task.Id, TaskState.Completed, final: true);
    }

    public AgentCard GetAgentCard(string agentUrl)
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
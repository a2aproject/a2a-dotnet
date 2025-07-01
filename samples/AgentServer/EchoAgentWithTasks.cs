using System.Diagnostics;
using A2A.Core;

public class EchoAgentWithTasks
{
    private ITaskManager? _TaskManager = null;

    public void Attach(TaskManager taskManager)
    {
        _TaskManager = taskManager;
        taskManager.OnTaskCreated = ProcessMessage;
        taskManager.OnTaskUpdated = ProcessMessage;
        taskManager.OnAgentCardQuery = GetAgentCard;
    }

    public async Task ProcessMessage(AgentTask task)
    {
        // Process the message
        var messageText = task.History!.Last().Parts.OfType<TextPart>().First().Text;

        await _TaskManager!.ReturnArtifactAsync(task.Id, new Artifact()
        {
            Parts = [new TextPart() {
                Text = $"Echo: {messageText}"
            }]
        });
        await _TaskManager!.UpdateStatusAsync(task.Id, TaskState.Completed,final: true);
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
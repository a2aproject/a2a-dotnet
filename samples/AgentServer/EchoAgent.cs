using A2A;

namespace AgentServer;

public class EchoAgent
{
    private ITaskManager? _taskManager;

    public void Attach(TaskManager taskManager)
    {
        _taskManager = taskManager;
        taskManager.OnMessageReceived = ProcessMessage;
        taskManager.OnAgentCardQuery = GetAgentCard;
    }

    public Task<Message> ProcessMessage(MessageSendParams messageSendParams)
    {
        // Process the message
        var messageText = messageSendParams.Message.Parts.OfType<TextPart>().First().Text;

        // Create and return an artifact
        var message = new Message()
        {
            Role = MessageRole.Agent,
            MessageId = Guid.NewGuid().ToString(),
            ContextId = messageSendParams.Message.ContextId,
            Parts = [new TextPart() {
                Text = $"Echo: {messageText}"
            }]
        };
        return Task.FromResult(message);
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
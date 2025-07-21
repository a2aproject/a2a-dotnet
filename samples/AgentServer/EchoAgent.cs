using A2A;

namespace AgentServer;

public class EchoAgent
{
    public void Attach(ITaskManager taskManager)
    {
        taskManager.OnMessageReceived = ProcessMessage;
        taskManager.OnAgentCardQuery = GetAgentCard;
    }

    private Task<Message> ProcessMessage(MessageSendParams messageSendParams, CancellationToken _)
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
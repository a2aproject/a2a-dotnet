using A2A;

namespace AgentServer;

public class EchoAgent
{
    public void Attach(ITaskManager taskManager)
    {
        taskManager.OnMessageReceived = ProcessMessageAsync;
    }

    private Task<A2AResponse> ProcessMessageAsync(MessageSendParams messageSendParams, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<A2AResponse>(cancellationToken);
        }

        // Process the message
        var messageText = messageSendParams.Message.Parts.OfType<TextPart>().First().Text;

        // Create and return an artifact
        var message = new AgentMessage()
        {
            Role = MessageRole.Agent,
            MessageId = Guid.NewGuid().ToString(),
            ContextId = messageSendParams.Message.ContextId,
            Parts = [new TextPart() {
                Text = $"Echo: {messageText}"
            }]
        };

        return Task.FromResult<A2AResponse>(message);
    }

    public AgentCard Card
    {
        get
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
                Version = "1.0.0",
                DefaultInputModes = ["text"],
                DefaultOutputModes = ["text"],
                Capabilities = capabilities,
                Skills = [],
            };
        }
    }
}
using A2A;

namespace AgentServer;

public class EchoAgent
{
    public void Attach(TaskManager taskManager)
    {
        taskManager.OnSendMessage = OnSendMessageAsync;
    }

    private Task<SendMessageResponse> OnSendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<SendMessageResponse>(cancellationToken);
        }

        var messageText = request.Message.Parts.FirstOrDefault(p => p.Text is not null)?.Text ?? string.Empty;

        var response = new Message
        {
            Role = Role.Agent,
            MessageId = Guid.NewGuid().ToString("N"),
            ContextId = request.Message.ContextId,
            Parts = [Part.FromText($"Echo: {messageText}")],
        };

        return Task.FromResult(new SendMessageResponse { Message = response });
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
                    Description = "Echoes back the user message.",
                    Tags = ["echo", "test"],
                }
            ],
        };
    }
}
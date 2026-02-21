using A2A;

namespace AgentServer;

public sealed class EchoAgent : IAgentHandler
{
    public async Task ExecuteAsync(AgentContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var reply = new Message
        {
            Role = Role.Agent,
            MessageId = Guid.NewGuid().ToString("N"),
            ContextId = context.ContextId,
            Parts = [Part.FromText($"Echo: {context.UserText}")],
        };
        await eventQueue.EnqueueMessageAsync(reply, cancellationToken);
    }

    public static AgentCard GetAgentCard(string agentUrl) =>
        new()
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
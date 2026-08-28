using A2A;

namespace A2AServer;

internal static class TravelAgentCard
{
    internal static AgentCard Create(string agentUrl) =>
        new()
        {
            Name = "TravelAgent",
            Description = "Plans trips using a local tour catalog and destination-local time.",
            Version = "1.0.0",
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["text/plain"],
            Capabilities = new()
            {
                Streaming = true,
                PushNotifications = false,
            },
            SupportedInterfaces =
            [
                new()
                {
                    Url = agentUrl,
                    ProtocolBinding = ProtocolBindingNames.JsonRpc,
                    ProtocolVersion = "1.0",
                },
                new()
                {
                    Url = agentUrl,
                    ProtocolBinding = ProtocolBindingNames.HttpJson,
                    ProtocolVersion = "1.0",
                },
            ],
        };
}

using A2A;

namespace AgentServer;

public class EchoAgent
{
    public void Attach(ITaskManager taskManager, IAgentCardProvider agentCardProvider)
    {
        taskManager.OnMessageReceived = ProcessMessageAsync;
        agentCardProvider.OnAgentCardQuery = GetAgentCardAsync;
        agentCardProvider.OnAuthenticatedAgentCardQuery = GetAuthenticatedAgentCardAsync;
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
            Description = "Agent which will echo every message it receives.",
            Url = agentUrl,
            Version = "1.0.0",
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Capabilities = capabilities,
            Skills = [],
            SupportsAuthenticatedExtendedCard = true, // Indicate support for authenticated extended cards
        });
    }

    private Task<AgentCard> GetAuthenticatedAgentCardAsync(string agentUrl, AuthenticationContext? authContext, CancellationToken cancellationToken)
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

        // Base skills available to all users
        var skills = new List<AgentSkill>
        {
            new()
            {
                Id = "echo",
                Name = "Echo Messages",
                Description = "Echoes back any message sent to the agent"
            }
        };

        // Add additional skills for authenticated users
        if (authContext?.IsAuthenticated == true)
        {
            skills.Add(new AgentSkill
            {
                Id = "admin-echo",
                Name = "Admin Echo",
                Description = "Enhanced echo functionality with user information (requires authentication)",
                Examples = ["Show my user info", "Echo with authentication details"]
            });

            // Add special admin skills for users with admin role
            if (authContext.HasClaim("role", "admin"))
            {
                skills.Add(new AgentSkill
                {
                    Id = "system-info",
                    Name = "System Information",
                    Description = "Provides system information and diagnostics (admin only)",
                    Examples = ["Show system status", "Get server information"]
                });
            }
        }

        return Task.FromResult(new AgentCard()
        {
            Name = "Echo Agent (Extended)",
            Description = authContext?.IsAuthenticated == true
                ? $"Enhanced echo agent with extended capabilities for authenticated user: {authContext.UserName}"
                : "Agent which will echo every message it receives.",
            Url = agentUrl,
            Version = "1.0.0",
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Capabilities = capabilities,
            Skills = skills,
            SupportsAuthenticatedExtendedCard = true,
        });
    }
}
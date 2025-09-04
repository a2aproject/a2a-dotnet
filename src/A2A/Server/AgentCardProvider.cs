namespace A2A;

/// <summary>
/// Default implementation of agent card provider for handling agent card queries.
/// </summary>
/// <remarks>
/// Provides a simple implementation that can be extended or replaced with custom logic
/// for returning agent cards based on authentication context and agent capabilities.
/// </remarks>
public sealed class AgentCardProvider : IAgentCardProvider
{
    /// <inheritdoc />
    public Func<string, CancellationToken, Task<AgentCard>> OnAgentCardQuery { get; set; }
        = static (agentUrl, ct) => ct.IsCancellationRequested
            ? Task.FromCanceled<AgentCard>(ct)
            : Task.FromResult(new AgentCard() { Name = "Unknown", Url = agentUrl });

    /// <inheritdoc />
    public Func<string, AuthenticationContext?, CancellationToken, Task<AgentCard>>? OnAuthenticatedAgentCardQuery { get; set; }
}
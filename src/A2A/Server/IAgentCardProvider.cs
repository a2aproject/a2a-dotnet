namespace A2A;

/// <summary>
/// Interface for providing agent card information and capabilities.
/// </summary>
/// <remarks>
/// Responsible for handling agent card queries, including both standard and authenticated extended agent cards.
/// This is separate from task management as agent cards represent agent capabilities and metadata,
/// not task lifecycle management.
/// </remarks>
public interface IAgentCardProvider
{
    /// <summary>
    /// Gets or sets the handler for when a standard agent card is queried.
    /// </summary>
    /// <remarks>
    /// Returns agent capability information for a given agent URL.
    /// </remarks>
    Func<string, CancellationToken, Task<AgentCard>> OnAgentCardQuery { get; set; }

    /// <summary>
    /// Gets or sets the handler for when an authenticated agent card is queried.
    /// </summary>
    /// <remarks>
    /// Returns extended agent capability information for a given agent URL when the user is authenticated.
    /// This allows agents to provide additional skills, capabilities, or metadata that require authentication.
    /// </remarks>
    Func<string, AuthenticationContext?, CancellationToken, Task<AgentCard>>? OnAuthenticatedAgentCardQuery { get; set; }
}
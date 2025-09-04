namespace A2A;

/// <summary>
/// Parameters for retrieving an agent card.
/// </summary>
/// <remarks>
/// Used for JSON-RPC method parameters for agent card retrieval operations.
/// </remarks>
public sealed class AgentCardParams
{
    /// <summary>
    /// Gets or sets the URL of the agent to retrieve the card for.
    /// </summary>
    /// <remarks>
    /// The agent URL identifies which agent's capabilities should be returned.
    /// </remarks>
    public required string AgentUrl { get; set; }
}
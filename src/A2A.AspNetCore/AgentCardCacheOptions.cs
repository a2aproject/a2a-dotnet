namespace A2A.AspNetCore;

/// <summary>
/// Configuration options for Agent Card HTTP caching.
/// </summary>
public sealed class AgentCardCacheOptions
{
    /// <summary>
    /// Gets or sets how long clients and shared caches may reuse the Agent Card.
    /// </summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(1);
}

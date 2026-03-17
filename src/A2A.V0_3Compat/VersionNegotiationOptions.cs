namespace A2A.V0_3Compat;

/// <summary>Configuration options for version negotiation.</summary>
public sealed class VersionNegotiationOptions
{
    /// <summary>Gets or sets whether fallback to v0.3 is allowed. Default is true.</summary>
    public bool AllowV03Fallback { get; set; } = true;

    /// <summary>Gets or sets the preferred protocol version. Default is "1.0".</summary>
    public string PreferredVersion { get; set; } = "1.0";

    /// <summary>Gets or sets the agent card path. Default is "/.well-known/agent-card.json".</summary>
    public string AgentCardPath { get; set; } = "/.well-known/agent-card.json";
}

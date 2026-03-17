namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents an agent card containing agent metadata and capabilities.</summary>
public sealed class AgentCard
{
    /// <summary>Gets or sets the agent name.</summary>
    [JsonRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the agent description.</summary>
    [JsonRequired]
    public string Description { get; set; } = string.Empty;

    /// <summary>Version of the agent.</summary>
    [JsonRequired]
    public string Version { get; set; } = string.Empty;

    /// <summary>URL for the agent's documentation.</summary>
    public string? DocumentationUrl { get; set; }

    /// <summary>URL for the agent's icon.</summary>
    public string? IconUrl { get; set; }

    /// <summary>Gets or sets the supported interfaces for this agent.</summary>
    [JsonRequired]
    public List<AgentInterface> SupportedInterfaces { get; set; } = [];

    /// <summary>Gets or sets the agent capabilities.</summary>
    [JsonRequired]
    public AgentCapabilities Capabilities { get; set; } = new();

    /// <summary>Gets or sets the agent provider information.</summary>
    public AgentProvider? Provider { get; set; }

    /// <summary>Gets or sets the skills offered by this agent.</summary>
    [JsonRequired]
    public List<AgentSkill> Skills { get; set; } = [];

    /// <summary>Gets or sets the default input modes.</summary>
    [JsonRequired]
    public List<string> DefaultInputModes { get; set; } = [];

    /// <summary>Gets or sets the default output modes.</summary>
    [JsonRequired]
    public List<string> DefaultOutputModes { get; set; } = [];

    /// <summary>Gets or sets the security schemes available for this agent.</summary>
    public Dictionary<string, SecurityScheme>? SecuritySchemes { get; set; }

    /// <summary>Gets or sets the security requirements for this agent.</summary>
    public List<SecurityRequirement>? SecurityRequirements { get; set; }

    /// <summary>Gets or sets the signatures for this agent card.</summary>
    public List<AgentCardSignature>? Signatures { get; set; }
}

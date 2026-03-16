namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents an agent card containing agent metadata and capabilities.</summary>
public sealed class AgentCard
{
    /// <summary>Gets or sets the agent name.</summary>
    [JsonPropertyName("name"), JsonRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the agent description.</summary>
    [JsonPropertyName("description"), JsonRequired]
    public string Description { get; set; } = string.Empty;

    /// <summary>Version of the agent.</summary>
    [JsonPropertyName("version"), JsonRequired]
    public string Version { get; set; } = string.Empty;

    /// <summary>URL for the agent's documentation.</summary>
    [JsonPropertyName("documentationUrl")]
    public string? DocumentationUrl { get; set; }

    /// <summary>URL for the agent's icon.</summary>
    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    /// <summary>Gets or sets the supported interfaces for this agent.</summary>
    [JsonPropertyName("supportedInterfaces"), JsonRequired]
    public List<AgentInterface> SupportedInterfaces { get; set; } = [];

    /// <summary>Gets or sets the agent capabilities.</summary>
    [JsonPropertyName("capabilities"), JsonRequired]
    public AgentCapabilities Capabilities { get; set; } = new();

    /// <summary>Gets or sets the agent provider information.</summary>
    [JsonPropertyName("provider")]
    public AgentProvider? Provider { get; set; }

    /// <summary>Gets or sets the skills offered by this agent.</summary>
    [JsonPropertyName("skills"), JsonRequired]
    public List<AgentSkill> Skills { get; set; } = [];

    /// <summary>Gets or sets the default input modes.</summary>
    [JsonPropertyName("defaultInputModes"), JsonRequired]
    public List<string> DefaultInputModes { get; set; } = [];

    /// <summary>Gets or sets the default output modes.</summary>
    [JsonPropertyName("defaultOutputModes"), JsonRequired]
    public List<string> DefaultOutputModes { get; set; } = [];

    /// <summary>Gets or sets the security schemes available for this agent.</summary>
    [JsonPropertyName("securitySchemes")]
    public Dictionary<string, SecurityScheme>? SecuritySchemes { get; set; }

    /// <summary>Gets or sets the security requirements for this agent.</summary>
    [JsonPropertyName("securityRequirements")]
    public List<SecurityRequirement>? SecurityRequirements { get; set; }

    /// <summary>Gets or sets the signatures for this agent card.</summary>
    [JsonPropertyName("signatures")]
    public List<AgentCardSignature>? Signatures { get; set; }
}

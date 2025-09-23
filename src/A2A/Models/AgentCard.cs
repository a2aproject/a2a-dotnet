using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// An AgentCard conveys key information about an agent.
/// </summary>
/// <remarks>
/// - Overall details (version, name, description, uses)
/// - Skills: A set of capabilities the agent can perform
/// - Default modalities/content types supported by the agent.
/// - Authentication requirements.
/// </remarks>
public sealed class AgentCard
{
    /// <summary>
    /// Creates a new instance of the <see cref="AgentCard"/> class.
    /// </summary>
    public AgentCard()
    {
    }

    // A constructor for cloning purposes.
    internal AgentCard(AgentCard source)
    {
        Name = source.Name;
        Description = source.Description;
        Url = source.Url;
        IconUrl = source.IconUrl;
        Provider = source.Provider is null ? new AgentProvider() : new AgentProvider(source.Provider);
        Version = source.Version;
        ProtocolVersion = source.ProtocolVersion;
        DocumentationUrl = source.DocumentationUrl;
        Capabilities = source.Capabilities is null ? new AgentCapabilities() : new AgentCapabilities(source.Capabilities);
        SecuritySchemes = source.SecuritySchemes is null ? null : new Dictionary<string, SecurityScheme>(source.SecuritySchemes, source.SecuritySchemes.Comparer);
        Security = source.Security;
        DefaultInputModes = [.. source.DefaultInputModes];
        DefaultOutputModes = [.. source.DefaultOutputModes];
        Skills = [.. source.Skills];
        SupportsAuthenticatedExtendedCard = source.SupportsAuthenticatedExtendedCard;
        AdditionalInterfaces = [.. source.AdditionalInterfaces];
        PreferredTransport = source.PreferredTransport.HasValue ? new AgentTransport(source.PreferredTransport.Value) : null;
    }

    /// <summary>
    /// Gets or sets the human readable name of the agent.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a human-readable description of the agent.
    /// </summary>
    /// <remarks>
    /// Used to assist users and other agents in understanding what the agent can do.
    /// CommonMark MAY be used for rich text formatting.
    /// (e.g., "This agent helps users find recipes, plan meals, and get cooking instructions.")
    /// </remarks>
    [JsonPropertyName("description")]
    [JsonRequired]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a URL to the address the agent is hosted at.
    /// </summary>
    /// <remarks>
    /// This represents the preferred endpoint as declared by the agent.
    /// </remarks>
    [JsonPropertyName("url")]
    [JsonRequired]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a URL to an icon for the agent. (e.g., `https://agent.example.com/icon.png`).
    /// </summary>
    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    /// <summary>
    /// Gets or sets the service provider of the agent.
    /// </summary>
    [JsonPropertyName("provider")]
    public AgentProvider? Provider { get; set; }

    /// <summary>
    /// Gets or sets the version of the agent - format is up to the provider.
    /// </summary>
    [JsonPropertyName("version")]
    [JsonRequired]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// The version of the A2A protocol this agent supports.
    /// </summary>
    [JsonPropertyName("protocolVersion")]
    [JsonRequired]
    public string ProtocolVersion { get; set; } = "0.3.0";

    /// <summary>
    /// Gets or sets a URL to documentation for the agent.
    /// </summary>
    [JsonPropertyName("documentationUrl")]
    public string? DocumentationUrl { get; set; }

    /// <summary>
    /// Gets or sets the optional capabilities supported by the agent.
    /// </summary>
    [JsonPropertyName("capabilities")]
    [JsonRequired]
    public AgentCapabilities Capabilities { get; set; } = new AgentCapabilities();

    /// <summary>
    /// Gets or sets the security scheme details used for authenticating with this agent.
    /// </summary>
    [JsonPropertyName("securitySchemes")]
    public Dictionary<string, SecurityScheme>? SecuritySchemes { get; set; }

    /// <summary>
    /// Gets or sets the security requirements for contacting the agent.
    /// </summary>
    [JsonPropertyName("security")]
    public List<Dictionary<string, string[]>>? Security { get; set; }

    /// <summary>
    /// Gets or sets the set of interaction modes that the agent supports across all skills.
    /// </summary>
    /// <remarks>
    /// This can be overridden per-skill. Supported media types for input.
    /// </remarks>
    [JsonPropertyName("defaultInputModes")]
    [JsonRequired]
    public List<string> DefaultInputModes { get; set; } = ["text"];

    /// <summary>
    /// Gets or sets the supported media types for output.
    /// </summary>
    [JsonPropertyName("defaultOutputModes")]
    [JsonRequired]
    public List<string> DefaultOutputModes { get; set; } = ["text"];

    /// <summary>
    /// Gets or sets the skills that are a unit of capability that an agent can perform.
    /// </summary>
    [JsonPropertyName("skills")]
    [JsonRequired]
    public List<AgentSkill> Skills { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the agent supports providing an extended agent card when the user is authenticated.
    /// </summary>
    /// <remarks>
    /// Defaults to false if not specified.
    /// </remarks>
    [JsonPropertyName("supportsAuthenticatedExtendedCard")]
    public bool SupportsAuthenticatedExtendedCard { get; set; } = false;

    /// <summary>
    /// Announcement of additional supported transports.
    /// </summary>
    /// <remarks>
    /// The client can use any of the supported transports.
    /// </remarks>
    [JsonPropertyName("additionalInterfaces")]
    public List<AgentInterface> AdditionalInterfaces { get; set; } = [];

    /// <summary>
    /// The transport of the preferred endpoint.
    /// </summary>
    /// <remarks>
    /// This property is required. It defaults to <see cref="AgentTransport.JsonRpc"/> when an <see cref="AgentCard"/> is instantiated in code.
    /// </remarks>
    [JsonPropertyName("preferredTransport")]
    [JsonRequired]
    public AgentTransport PreferredTransport { get; set; } = AgentTransport.JsonRpc;

    /// <summary>
    /// JSON Web Signatures computed for this AgentCard.
    /// </summary>
    [JsonPropertyName("signatures")]
    public List<AgentCardSignature>? Signatures { get; set; }
}

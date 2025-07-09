using System.ComponentModel.DataAnnotations;
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
public class AgentCard
{
    /// <summary>
    /// Gets or sets the human readable name of the agent.
    /// </summary>
    [JsonPropertyName("name")]
    [Required]
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
    [Required]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a URL to the address the agent is hosted at.
    /// </summary>
    /// <remarks>
    /// This represents the preferred endpoint as declared by the agent.
    /// </remarks>
    [JsonPropertyName("url")]
    [Required]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the service provider of the agent.
    /// </summary>
    [JsonPropertyName("provider")]
    public AgentProvider? Provider { get; set; }

    /// <summary>
    /// Gets or sets the version of the agent - format is up to the provider.
    /// </summary>
    [JsonPropertyName("version")]
    [Required]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// The version of the A2A protocol this agent supports.
    /// </summary>
    [JsonPropertyName("protocolVersion")]
    [Required]
    public string ProtocolVersion { get; set; } = "0.2.3";

    /// <summary>
    /// Gets or sets a URL to documentation for the agent.
    /// </summary>
    [JsonPropertyName("documentationUrl")]
    public string? DocumentationUrl { get; set; }

    /// <summary>
    /// Gets or sets the optional capabilities supported by the agent.
    /// </summary>
    [JsonPropertyName("capabilities")]
    [Required]
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
    public Dictionary<string, string[]>? Security { get; set; }

    /// <summary>
    /// Gets or sets the set of interaction modes that the agent supports across all skills.
    /// </summary>
    /// <remarks>
    /// This can be overridden per-skill. Supported media types for input.
    /// </remarks>
    [JsonPropertyName("defaultInputModes")]
    public List<string> DefaultInputModes { get; set; } = ["text"];

    /// <summary>
    /// Gets or sets the supported media types for output.
    /// </summary>
    [JsonPropertyName("defaultOutputModes")]
    public List<string> DefaultOutputModes { get; set; } = ["text"];

    /// <summary>
    /// Gets or sets the skills that are a unit of capability that an agent can perform.
    /// </summary>
    [JsonPropertyName("skills")]
    [Required]
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
    public List<AgentInterface>? AdditionalInterfaces { get; set; }

    /// <summary>
    /// The transport of the preferred endpoint.
    /// </summary>
    /// <remarks>
    /// If empty, defaults to JSONRPC.
    /// </remarks>
    [JsonPropertyName("preferredTransport")]
    public AgentTransport? PreferredTransport { get; set; }
}

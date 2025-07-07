using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// An AgentCard conveys key information:
/// - Overall details (version, name, description, uses)
/// - Skills: A set of capabilities the agent can perform
/// - Default modalities/content types supported by the agent.
/// - Authentication requirements
/// </summary>
public class AgentCard
{
    /// <summary>
    /// Human readable name of the agent.
    /// </summary>
    [JsonPropertyName("name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// A human-readable description of the agent. Used to assist users and
    /// other agents in understanding what the agent can do.
    /// [CommonMark](https://commonmark.org/) MAY be used for rich text formatting.
    /// (e.g., "This agent helps users find recipes, plan meals, and get cooking instructions.")
    /// </summary>
    [JsonPropertyName("description")]
    [Required]
    public string? Description { get; set; }

    /// <summary>
    /// A URL to the address the agent is hosted at. This represents the
    /// preferred endpoint as declared by the agent.
    /// </summary>
    [JsonPropertyName("url")]
    [Required]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The service provider of the agent
    /// </summary>
    [JsonPropertyName("provider")]
    public AgentProvider? Provider { get; set; }

    /// <summary>
    /// The version of the agent - format is up to the provider.
    /// </summary>
    [JsonPropertyName("version")]
    [Required]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// A URL to documentation for the agent.
    /// </summary>
    [JsonPropertyName("documentationUrl")]
    public string? DocumentationUrl { get; set; }

    /// <summary>
    /// Optional capabilities supported by the agent.
    /// </summary>
    [JsonPropertyName("capabilities")]
    [Required]
    public AgentCapabilities Capabilities { get; set; } = new AgentCapabilities();

    /// <summary>
    /// Security scheme details used for authenticating with this agent.
    /// </summary>
    [JsonPropertyName("securitySchemes")]
    public Dictionary<string, SecurityScheme>? SecuritySchemes { get; set; }

    /// <summary>
    /// Security requirements for contacting the agent.
    /// </summary>
    [JsonPropertyName("security")]
    public Dictionary<string, string[]>? Security { get; set; }

    /// <summary>
    /// The set of interaction modes that the agent supports across all skills. This can be overridden per-skill.
    /// Supported media types for input.
    /// </summary>
    [JsonPropertyName("defaultInputModes")]
    public List<string> DefaultInputModes { get; set; } = ["text"];

    /// <summary>
    /// Supported media types for output.
    /// </summary>
    [JsonPropertyName("defaultOutputModes")]
    public List<string> DefaultOutputModes { get; set; } = ["text"];

    /// <summary>
    /// Skills are a unit of capability that an agent can perform.
    /// </summary>
    [JsonPropertyName("skills")]
    [Required]
    public List<AgentSkill> Skills { get; set; } = [];

    /// <summary>
    /// true if the agent supports providing an extended agent card when the user is authenticated.
    /// Defaults to false if not specified.
    /// </summary>
    [JsonPropertyName("supportsAuthenticatedExtendedCard")]
    public bool SupportsAuthenticatedExtendedCard { get; set; } = false;
}

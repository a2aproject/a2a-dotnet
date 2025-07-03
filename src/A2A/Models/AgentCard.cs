using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Information about an agent
/// </summary>
public class AgentCard
{
    /// <summary>
    /// Human-readable name of the agent (e.g., "Recipe Advisor Agent").
    /// </summary>
    [JsonPropertyName("name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// A human-readable description of the agent and its general purpose.
    /// [CommonMark](https://commonmark.org/) MAY be used for rich text formatting.
    /// (e.g., "This agent helps users find recipes, plan meals, and get cooking instructions.")
    /// </summary>
    [JsonPropertyName("description")]
    [Required]
    public string? Description { get; set; }

    /// <summary>
    /// The base URL endpoint for the agent's A2A service (where JSON-RPC requests are sent).
    /// Must be an absolute HTTPS URL for production (e.g., `https://agent.example.com/a2a/api`).
    /// HTTP MAY be used for local development/testing only.
    /// </summary>
    [JsonPropertyName("url")]
    [Required]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Information about the organization or entity providing the agent.
    /// </summary>
    [JsonPropertyName("provider")]
    public AgentProvider? Provider { get; set; }

    /// <summary>
    /// Version string for the agent or its A2A implementation
    /// (format is defined by the provider, e.g., "1.0.0", "2023-10-26-beta").
    /// </summary>
    [JsonPropertyName("version")]
    [Required]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Optional documentation URL
    /// </summary>
    [JsonPropertyName("documentationUrl")]
    public string? DocumentationUrl { get; set; }

    /// <summary>
    /// The agent capabilities
    /// </summary>
    [JsonPropertyName("capabilities")]
    [Required]
    public AgentCapabilities Capabilities { get; set; } = new AgentCapabilities();

    /// <summary>
    /// Optional authentication information
    /// </summary>
    [JsonPropertyName("securitySchemes")]
    public Dictionary<string, SecurityScheme>? SecuritySchemes { get; set; }

    public Dictionary<string, string[]>? Security { get; set; }

    /// <summary>
    /// Default input modes supported
    /// </summary>
    [JsonPropertyName("defaultInputModes")]
    public List<string> DefaultInputModes { get; set; } = ["text"];

    /// <summary>
    /// Default output modes supported
    /// </summary>
    [JsonPropertyName("defaultOutputModes")]
    public List<string> DefaultOutputModes { get; set; } = ["text"];

    /// <summary>
    /// The skills provided by this agent
    /// </summary>
    [JsonPropertyName("skills")]
    [Required]
    public List<AgentSkill> Skills { get; set; } = [];

    /// <summary>
    /// Indicates support for retrieving a more detailed Agent Card via an authenticated endpoint.
    /// </summary>
    [JsonPropertyName("supportsAuthenticatedExtendedCard")]
    public bool SupportsAuthenticatedExtendedCard { get; set; } = false;
}

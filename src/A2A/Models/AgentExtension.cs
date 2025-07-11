using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// A declaration of an extension supported by an Agent.
/// </summary>
public sealed class AgentExtension
{
    /// <summary>
    /// Gets or sets the URI of the extension.
    /// </summary>
    [JsonPropertyName("uri")]
    [JsonRequired]
    public string? Uri { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a description of how this agent uses this extension.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets whether the client must follow specific requirements of the extension.
    /// </summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; } = false;

    /// <summary>
    /// Gets or sets optional configuration for the extension.
    /// </summary>
    [JsonPropertyName("params")]
    public Dictionary<string, JsonElement>? Params { get; set; }
}

namespace A2A;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Represents an extension supported by an agent.</summary>
public sealed class AgentExtension
{
    /// <summary>Gets or sets the URI identifying this extension.</summary>
    [JsonPropertyName("uri"), JsonRequired]
    public string Uri { get; set; } = string.Empty;

    /// <summary>Gets or sets the description of this extension.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Gets or sets whether this extension is required.</summary>
    [JsonPropertyName("required")]
    public bool? Required { get; set; }

    /// <summary>Gets or sets the parameters for this extension.</summary>
    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

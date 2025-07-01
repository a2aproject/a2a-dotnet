using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace A2A.Core;

/// <summary>
/// Information about the agent provider
/// </summary>
public class AgentProvider
{
    /// <summary>
    /// The organization name
    /// </summary>
    [JsonPropertyName("organization")]
    [Required]
    public string Organization { get; set; } = string.Empty;

    /// <summary>
    /// Optional URL for the provider
    /// </summary>
    [JsonPropertyName("url")]
    [Required]
    public string? Url { get; set; }
}

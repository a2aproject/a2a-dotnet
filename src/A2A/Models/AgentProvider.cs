using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents the service provider of an agent.
/// </summary>
public class AgentProvider
{
    /// <summary>
    /// Agent provider's organization name.
    /// </summary>
    [JsonPropertyName("organization")]
    [Required]
    public string Organization { get; set; } = string.Empty;

    /// <summary>
    /// Agent provider's URL.
    /// </summary>
    [JsonPropertyName("url")]
    [Required]
    public string Url { get; set; } = string.Empty;
}

using System.Text.Json.Serialization;

namespace A2A.V0_3;

/// <summary>
/// Represents the service provider of an agent.
/// </summary>
public sealed class AgentProvider
{
    /// <summary>
    /// Agent provider's organization name.
    /// </summary>
    [JsonPropertyName("organization")]
    [JsonRequired]
    public string Organization { get; set; } = string.Empty;

    /// <summary>
    /// Agent provider's URL.
    /// </summary>
    [JsonPropertyName("url")]
    [JsonRequired]
    public string Url { get; set; } = string.Empty;
}

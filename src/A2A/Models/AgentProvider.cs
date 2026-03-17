using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents the service provider of an agent.
/// </summary>
public sealed class AgentProvider
{
    /// <summary>
    /// Agent provider's organization name.
    /// </summary>
    [JsonRequired]
    public string Organization { get; set; } = string.Empty;

    /// <summary>
    /// Agent provider's URL.
    /// </summary>
    [JsonRequired]
    public string Url { get; set; } = string.Empty;
}

using System.Text.Json.Serialization;

namespace A2A.V0_3;

/// <summary>
/// Provides a declaration of a combination of target URL and supported transport to interact with an agent.
/// </summary>
public sealed class AgentInterface
{
    /// <summary>
    /// The transport supported by this URL.
    /// </summary>
    /// <remarks>
    /// This is an open form string, to be easily extended for many transport protocols.
    /// The core ones officially supported are JSONRPC, GRPC, and HTTP+JSON.
    /// </remarks>
    [JsonPropertyName("transport")]
    [JsonRequired]
    public required AgentTransport Transport { get; set; }

    /// <summary>
    /// The target URL for the agent interface.
    /// </summary>
    [JsonPropertyName("url")]
    [JsonRequired]
    public required string Url { get; set; }
}
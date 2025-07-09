using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Provides a declaration of a combination of target URL and supported transport to interact with an agent.
/// </summary>
public class AgentInterface
{
    /// <summary>
    /// The transport supported by this URL.
    /// </summary>
    /// <remarks>
    /// This is an open form string, to be easily extended for many transport protocols.
    /// The core ones officially supported are JSONRPC, GRPC, and HTTP+JSON.
    /// </remarks>
    [JsonPropertyName("transport")]
    [Required]
    public required AgentTransport Transport { get; set; }

    /// <summary>
    /// The target URL for the agent interface.
    /// </summary>
    [JsonPropertyName("url")]
    [Required]
    public required string Url { get; set; }
}
namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents an interface supported by an agent.</summary>
public sealed class AgentInterface
{
    /// <summary>Gets or sets the URL for this interface.</summary>
    [JsonPropertyName("url"), JsonRequired]
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the protocol binding.</summary>
    [JsonPropertyName("protocolBinding"), JsonRequired]
    public string ProtocolBinding { get; set; } = "JSONRPC";

    /// <summary>Gets or sets the tenant identifier.</summary>
    [JsonPropertyName("tenant")]
    public string? Tenant { get; set; }

    /// <summary>Gets or sets the protocol version.</summary>
    [JsonPropertyName("protocolVersion"), JsonRequired]
    public string ProtocolVersion { get; set; } = "1.0";
}
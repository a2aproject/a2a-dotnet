namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents an interface supported by an agent.</summary>
public sealed class AgentInterface
{
    /// <summary>Gets or sets the URL for this interface.</summary>
    [JsonRequired]
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the protocol binding.</summary>
    [JsonRequired]
    public string ProtocolBinding { get; set; } = "JSONRPC";

    /// <summary>Gets or sets the tenant identifier.</summary>
    public string? Tenant { get; set; }

    /// <summary>Gets or sets the protocol version.</summary>
    [JsonRequired]
    public string ProtocolVersion { get; set; } = "1.0";
}
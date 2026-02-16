namespace A2A;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Represents a request to send a message.</summary>
public sealed class SendMessageRequest
{
    /// <summary>Gets or sets the tenant identifier.</summary>
    [JsonPropertyName("tenant")]
    public string? Tenant { get; set; }

    /// <summary>Gets or sets the message to send.</summary>
    [JsonPropertyName("message"), JsonRequired]
    public Message Message { get; set; } = new();

    /// <summary>Gets or sets the configuration for the request.</summary>
    [JsonPropertyName("configuration")]
    public SendMessageConfiguration? Configuration { get; set; }

    /// <summary>Gets or sets the metadata associated with this request.</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

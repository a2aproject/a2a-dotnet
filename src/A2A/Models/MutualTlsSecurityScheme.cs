namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a mutual TLS security scheme.</summary>
public sealed class MutualTlsSecurityScheme
{
    /// <summary>Gets or sets the description of the mutual TLS scheme.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

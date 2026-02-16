namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents an HTTP authentication security scheme.</summary>
public sealed class HttpAuthSecurityScheme
{
    /// <summary>Description of the HTTP auth security scheme.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Gets or sets the authentication scheme (e.g., bearer, basic).</summary>
    [JsonPropertyName("scheme"), JsonRequired]
    public string Scheme { get; set; } = string.Empty;

    /// <summary>Gets or sets the bearer format.</summary>
    [JsonPropertyName("bearerFormat")]
    public string? BearerFormat { get; set; }
}

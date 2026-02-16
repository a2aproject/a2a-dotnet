namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents an OAuth2 security scheme.</summary>
public sealed class OAuth2SecurityScheme
{
    /// <summary>Description of the OAuth2 security scheme.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Gets or sets the OAuth2 flows.</summary>
    [JsonPropertyName("flows"), JsonRequired]
    public OAuthFlows Flows { get; set; } = new();

    /// <summary>URL for OAuth2 metadata discovery.</summary>
    [JsonPropertyName("oauth2MetadataUrl")]
    public string? OAuth2MetadataUrl { get; set; }
}

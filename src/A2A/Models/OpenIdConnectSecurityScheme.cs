namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents an OpenID Connect security scheme.</summary>
public sealed class OpenIdConnectSecurityScheme
{
    /// <summary>Description of the OpenID Connect security scheme.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Gets or sets the OpenID Connect URL.</summary>
    [JsonPropertyName("openIdConnectUrl"), JsonRequired]
    public string OpenIdConnectUrl { get; set; } = string.Empty;
}

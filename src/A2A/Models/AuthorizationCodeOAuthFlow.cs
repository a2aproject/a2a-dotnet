namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents an OAuth2 authorization code flow.</summary>
public sealed class AuthorizationCodeOAuthFlow
{
    /// <summary>Gets or sets the authorization URL.</summary>
    [JsonPropertyName("authorizationUrl"), JsonRequired]
    public string AuthorizationUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the token URL.</summary>
    [JsonPropertyName("tokenUrl"), JsonRequired]
    public string TokenUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the refresh URL.</summary>
    [JsonPropertyName("refreshUrl")]
    public string? RefreshUrl { get; set; }

    /// <summary>Gets or sets the available scopes.</summary>
    [JsonPropertyName("scopes"), JsonRequired]
    public Dictionary<string, string> Scopes { get; set; } = new();

    /// <summary>Whether PKCE is required for the authorization code flow.</summary>
    [JsonPropertyName("pkceRequired")]
    public bool? PkceRequired { get; set; }
}

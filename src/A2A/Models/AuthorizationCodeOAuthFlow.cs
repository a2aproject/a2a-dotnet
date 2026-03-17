namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents an OAuth2 authorization code flow.</summary>
public sealed class AuthorizationCodeOAuthFlow
{
    /// <summary>Gets or sets the authorization URL.</summary>
    [JsonRequired]
    public string AuthorizationUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the token URL.</summary>
    [JsonRequired]
    public string TokenUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the refresh URL.</summary>
    public string? RefreshUrl { get; set; }

    /// <summary>Gets or sets the available scopes.</summary>
    [JsonRequired]
    public Dictionary<string, string> Scopes { get; set; } = new();

    /// <summary>Whether PKCE is required for the authorization code flow.</summary>
    public bool? PkceRequired { get; set; }
}

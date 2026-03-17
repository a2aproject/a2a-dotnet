namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents an OAuth2 implicit flow.</summary>
[Obsolete("Implicit flow is deprecated.")]
public sealed class ImplicitOAuthFlow
{
    /// <summary>Gets or sets the authorization URL.</summary>
    [JsonRequired]
    public string AuthorizationUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the refresh URL.</summary>
    public string? RefreshUrl { get; set; }

    /// <summary>Gets or sets the available scopes.</summary>
    [JsonRequired]
    public Dictionary<string, string> Scopes { get; set; } = new();
}

namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents an OAuth2 password flow.</summary>
[Obsolete("Password flow is deprecated.")]
public sealed class PasswordOAuthFlow
{
    /// <summary>Gets or sets the token URL.</summary>
    [JsonPropertyName("tokenUrl"), JsonRequired]
    public string TokenUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the refresh URL.</summary>
    [JsonPropertyName("refreshUrl")]
    public string? RefreshUrl { get; set; }

    /// <summary>Gets or sets the available scopes.</summary>
    [JsonPropertyName("scopes"), JsonRequired]
    public Dictionary<string, string> Scopes { get; set; } = new();
}

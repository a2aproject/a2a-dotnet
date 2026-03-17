namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents an OAuth2 device code flow.</summary>
public sealed class DeviceCodeOAuthFlow
{
    /// <summary>Gets or sets the device authorization URL.</summary>
    [JsonRequired]
    public string DeviceAuthorizationUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the token URL.</summary>
    [JsonRequired]
    public string TokenUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the refresh URL.</summary>
    public string? RefreshUrl { get; set; }

    /// <summary>Gets or sets the available scopes.</summary>
    [JsonRequired]
    public Dictionary<string, string> Scopes { get; set; } = new();
}

namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a push notification configuration.</summary>
public sealed class PushNotificationConfig
{
    /// <summary>Unique identifier for the push notification configuration.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the URL for push notifications.</summary>
    [JsonPropertyName("url"), JsonRequired]
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the authentication information.</summary>
    [JsonPropertyName("authentication")]
    public AuthenticationInfo? Authentication { get; set; }

    /// <summary>Gets or sets the token for push notifications.</summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}
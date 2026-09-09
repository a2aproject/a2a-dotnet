namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a push notification configuration (v0.3 shape).</summary>
/// <remarks>
/// In v1.0, the flat <see cref="TaskPushNotificationConfig"/> is preferred.
/// This class is retained for backward compatibility with v0.3 and the HTTP+JSON endpoints.
/// </remarks>
public sealed class PushNotificationConfig
{
    /// <summary>Unique identifier for the push notification configuration.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the URL for push notifications.</summary>
    [JsonRequired]
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the authentication information.</summary>
    public AuthenticationInfo? Authentication { get; set; }

    /// <summary>Gets or sets the token for push notifications.</summary>
    public string? Token { get; set; }
}
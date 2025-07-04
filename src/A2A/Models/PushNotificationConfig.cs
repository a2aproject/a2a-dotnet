using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents configuration for push notifications in A2A communication.
/// </summary>
public class PushNotificationConfig
{
    /// <summary>
    /// Gets or sets the URL where push notifications should be sent.
    /// </summary>
    [JsonPropertyName("url")]
    [JsonRequired]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional token for authenticating push notifications.
    /// </summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets detailed authentication information for push notifications.
    /// </summary>
    [JsonPropertyName("authentication")]
    public AuthenticationInfo? Authentication { get; set; }
}
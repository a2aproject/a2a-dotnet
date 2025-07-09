using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Configuration for setting up push notifications for task updates.
/// </summary>
public sealed class PushNotificationConfig
{
    /// <summary>
    /// URL for sending the push notifications.
    /// </summary>
    [JsonPropertyName("url")]
    [JsonRequired]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Optional server-generated identifier for the push notification configuration to support multiple callbacks.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Token unique to this task/session.
    /// </summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    /// <summary>
    /// Authentication details for push notifications.
    /// </summary>
    [JsonPropertyName("authentication")]
    public PushNotificationAuthenticationInfo? Authentication { get; set; }
}
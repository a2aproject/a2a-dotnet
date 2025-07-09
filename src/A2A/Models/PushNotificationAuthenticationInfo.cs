using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Defines authentication details for push notifications.
/// </summary>
public sealed class PushNotificationAuthenticationInfo
{
    /// <summary>
    /// Supported authentication schemes - e.g. Basic, Bearer.
    /// </summary>
    [JsonPropertyName("schemes")]
    [JsonRequired]
    public List<string> Schemes { get; set; } = [];

    /// <summary>
    /// Optional credentials.
    /// </summary>
    [JsonPropertyName("credentials")]
    public string? Credentials { get; set; }
}
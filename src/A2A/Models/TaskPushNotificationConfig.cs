using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Parameters for setting or getting push notification configuration for a task
/// </summary>
public class TaskPushNotificationConfig
{
    /// <summary>
    /// Task id.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Push notification configuration.
    /// </summary>
    [JsonPropertyName("pushNotificationConfig")]
    public PushNotificationConfig PushNotificationConfig { get; set; } = new PushNotificationConfig();
}
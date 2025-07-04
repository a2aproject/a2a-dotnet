using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents push notification configuration settings for a specific task.
/// </summary>
public class TaskPushNotificationConfig
{
    /// <summary>
    /// Gets or sets the unique identifier of the task associated with this push notification configuration.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the push notification configuration details.
    /// </summary>
    [JsonPropertyName("pushNotificationConfig")]
    public PushNotificationConfig PushNotificationConfig { get; set; } = new PushNotificationConfig();
}
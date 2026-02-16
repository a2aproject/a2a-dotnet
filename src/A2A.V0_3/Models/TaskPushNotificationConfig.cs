using System.Text.Json.Serialization;

namespace A2A.V0_3;

/// <summary>
/// Parameters for setting or getting push notification configuration for a task.
/// </summary>
public sealed class TaskPushNotificationConfig
{
    /// <summary>
    /// Task id.
    /// </summary>
    [JsonPropertyName("taskId")]
    [JsonRequired]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// Push notification configuration.
    /// </summary>
    [JsonPropertyName("pushNotificationConfig")]
    [JsonRequired]
    public PushNotificationConfig PushNotificationConfig { get; set; } = new PushNotificationConfig();
}
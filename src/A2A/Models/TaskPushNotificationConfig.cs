namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a task-specific push notification configuration.</summary>
public sealed class TaskPushNotificationConfig
{
    /// <summary>Gets or sets the configuration identifier.</summary>
    [JsonPropertyName("id"), JsonRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the task identifier.</summary>
    [JsonPropertyName("taskId"), JsonRequired]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>Gets or sets the push notification configuration.</summary>
    [JsonPropertyName("pushNotificationConfig"), JsonRequired]
    public PushNotificationConfig PushNotificationConfig { get; set; } = new();

    /// <summary>Gets or sets the tenant identifier.</summary>
    [JsonPropertyName("tenant")]
    public string? Tenant { get; set; }
}
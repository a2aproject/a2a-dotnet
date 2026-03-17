namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a task-specific push notification configuration.</summary>
public sealed class TaskPushNotificationConfig
{
    /// <summary>Gets or sets the configuration identifier.</summary>
    [JsonRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the task identifier.</summary>
    [JsonRequired]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>Gets or sets the push notification configuration.</summary>
    [JsonRequired]
    public PushNotificationConfig PushNotificationConfig { get; set; } = new();

    /// <summary>Gets or sets the tenant identifier.</summary>
    public string? Tenant { get; set; }
}
namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a request to create a push notification configuration.</summary>
public sealed class CreateTaskPushNotificationConfigRequest
{
    /// <summary>Gets or sets the tenant identifier.</summary>
    [JsonPropertyName("tenant")]
    public string? Tenant { get; set; }

    /// <summary>Gets or sets the task identifier.</summary>
    [JsonPropertyName("taskId"), JsonRequired]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>Unique identifier for the configuration.</summary>
    [JsonPropertyName("configId"), JsonRequired]
    public string ConfigId { get; set; } = string.Empty;

    /// <summary>Gets or sets the push notification configuration.</summary>
    [JsonPropertyName("config"), JsonRequired]
    public PushNotificationConfig Config { get; set; } = new();
}

namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a request to create a push notification configuration.</summary>
public sealed class CreateTaskPushNotificationConfigRequest
{
    /// <summary>Gets or sets the tenant identifier.</summary>
    public string? Tenant { get; set; }

    /// <summary>Gets or sets the task identifier.</summary>
    [JsonRequired]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>Unique identifier for the configuration.</summary>
    [JsonRequired]
    public string ConfigId { get; set; } = string.Empty;

    /// <summary>Gets or sets the push notification configuration.</summary>
    [JsonRequired]
    public PushNotificationConfig Config { get; set; } = new();
}

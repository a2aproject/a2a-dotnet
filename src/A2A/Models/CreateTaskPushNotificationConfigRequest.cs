namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a request to create a push notification configuration.</summary>
public sealed class CreateTaskPushNotificationConfigRequest
{
    /// <summary>Gets or sets the push notification configuration to create.</summary>
    [JsonRequired]
    public TaskPushNotificationConfig Config { get; set; } = new();
}

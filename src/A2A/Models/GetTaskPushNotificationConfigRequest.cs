namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a request to get a push notification configuration.</summary>
public sealed class GetTaskPushNotificationConfigRequest
{
    /// <summary>Gets or sets the tenant identifier.</summary>
    public string? Tenant { get; set; }

    /// <summary>Gets or sets the push notification configuration identifier.</summary>
    [JsonRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the task identifier.</summary>
    [JsonRequired]
    public string TaskId { get; set; } = string.Empty;
}

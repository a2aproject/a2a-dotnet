namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a task-specific push notification configuration matching the v1 spec's flat structure.</summary>
public sealed class TaskPushNotificationConfig
{
    /// <summary>Gets or sets the configuration identifier.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the task identifier.</summary>
    public string? TaskId { get; set; }

    /// <summary>Gets or sets the URL for push notifications.</summary>
    [JsonRequired]
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the token for push notifications.</summary>
    public string? Token { get; set; }

    /// <summary>Gets or sets the authentication information.</summary>
    public AuthenticationInfo? Authentication { get; set; }

    /// <summary>Gets or sets the tenant identifier.</summary>
    public string? Tenant { get; set; }
}
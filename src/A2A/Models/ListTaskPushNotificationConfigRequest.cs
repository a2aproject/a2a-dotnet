namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a request to list push notification configurations.</summary>
public sealed class ListTaskPushNotificationConfigRequest
{
    /// <summary>Gets or sets the tenant identifier.</summary>
    public string? Tenant { get; set; }

    /// <summary>Gets or sets the task identifier.</summary>
    [JsonRequired]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>Maximum number of configs to return.</summary>
    public int? PageSize { get; set; }

    /// <summary>Token for cursor-based pagination.</summary>
    public string? PageToken { get; set; }
}

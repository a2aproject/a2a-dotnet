namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a request to subscribe to task updates.</summary>
public sealed class SubscribeToTaskRequest
{
    /// <summary>Gets or sets the tenant identifier.</summary>
    [JsonPropertyName("tenant")]
    public string? Tenant { get; set; }

    /// <summary>Gets or sets the task identifier.</summary>
    [JsonPropertyName("id"), JsonRequired]
    public string Id { get; set; } = string.Empty;
}

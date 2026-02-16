namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a request to get a task by ID.</summary>
public sealed class GetTaskRequest
{
    /// <summary>Gets or sets the tenant identifier.</summary>
    [JsonPropertyName("tenant")]
    public string? Tenant { get; set; }

    /// <summary>Gets or sets the task identifier.</summary>
    [JsonPropertyName("id"), JsonRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the history length to include.</summary>
    [JsonPropertyName("historyLength")]
    public int? HistoryLength { get; set; }
}

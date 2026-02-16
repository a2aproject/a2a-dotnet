namespace A2A;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Represents a task status update event in the A2A protocol.</summary>
public sealed class TaskStatusUpdateEvent
{
    /// <summary>Gets or sets the task identifier.</summary>
    [JsonPropertyName("taskId"), JsonRequired]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>Gets or sets the context identifier.</summary>
    [JsonPropertyName("contextId"), JsonRequired]
    public string ContextId { get; set; } = string.Empty;

    /// <summary>Gets or sets the updated task status.</summary>
    [JsonPropertyName("status"), JsonRequired]
    public TaskStatus Status { get; set; } = new();

    /// <summary>Gets or sets the metadata associated with this event.</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}
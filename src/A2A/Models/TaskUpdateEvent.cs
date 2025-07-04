using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents an abstract base class for task update events that occur during task execution.
/// </summary>
public abstract class TaskUpdateEvent : A2AEvent
{
    /// <summary>
    /// Gets or sets the unique identifier of the task that this event is associated with.
    /// </summary>
    [JsonPropertyName("taskId")]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the context identifier that groups related tasks together.
    /// </summary>
    [JsonPropertyName("contextId")]
    public string? ContextId { get; set; }

    /// <summary>
    /// Gets or sets additional metadata associated with this task update event.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

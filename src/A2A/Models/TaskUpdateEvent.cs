using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Base class for task update events.
/// </summary>
public abstract class TaskUpdateEvent : A2AEvent
{
    /// <summary>
    /// Gets or sets the task ID
    /// </summary>
    [JsonPropertyName("taskId")]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the context the task is associated with
    /// </summary>
    [JsonPropertyName("contextId")]
    public string? ContextId { get; set; }

    /// <summary>
    /// Gets or sets the extension metadata
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

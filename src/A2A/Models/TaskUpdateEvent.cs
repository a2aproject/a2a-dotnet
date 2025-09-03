using System.Text.Json;
using System.Text.Json.Serialization;
using static A2A.A2AEvent;

namespace A2A;

/// <summary>
/// Base class for task update events.
/// </summary>
/// <param name="kind">The <c>kind</c> discriminator value</param>
public abstract class TaskUpdateEvent(A2AEventKind kind) : A2AEvent(kind)
{
    /// <summary>
    /// Gets or sets the task ID.
    /// </summary>
    [JsonPropertyName("taskId")]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the context the task is associated with.
    /// </summary>
    [JsonPropertyName("contextId")]
    [JsonRequired]
    public string ContextId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the extension metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

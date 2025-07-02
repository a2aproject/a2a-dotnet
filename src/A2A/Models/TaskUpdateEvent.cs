using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;


public abstract class TaskUpdateEvent : A2AEvent
{
    [JsonPropertyName("taskId")]
    public string TaskId { get; set; } = string.Empty;

    [JsonPropertyName("contextId")]
    public string? ContextId { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

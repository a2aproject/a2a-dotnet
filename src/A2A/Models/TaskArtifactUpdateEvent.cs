namespace A2A;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Represents a task artifact update event in the A2A protocol.</summary>
public sealed class TaskArtifactUpdateEvent
{
    /// <summary>Gets or sets the task identifier.</summary>
    [JsonPropertyName("taskId"), JsonRequired]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>Gets or sets the context identifier.</summary>
    [JsonPropertyName("contextId"), JsonRequired]
    public string ContextId { get; set; } = string.Empty;

    /// <summary>Gets or sets the artifact being updated.</summary>
    [JsonPropertyName("artifact"), JsonRequired]
    public Artifact Artifact { get; set; } = new();

    /// <summary>Gets or sets whether this update appends to the existing artifact.</summary>
    [JsonPropertyName("append")]
    public bool Append { get; set; }

    /// <summary>Gets or sets whether this is the last chunk of the artifact.</summary>
    [JsonPropertyName("lastChunk")]
    public bool LastChunk { get; set; }

    /// <summary>Gets or sets the metadata associated with this event.</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}
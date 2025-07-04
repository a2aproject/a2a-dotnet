using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents an event that occurs when an artifact is added or updated for a task.
/// </summary>
public class TaskArtifactUpdateEvent : TaskUpdateEvent
{
    /// <summary>
    /// Gets or sets the artifact that was added or updated.
    /// </summary>
    [JsonPropertyName("artifact")]
    public Artifact Artifact { get; set; } = new Artifact();

    /// <summary>
    /// Gets or sets a value indicating whether the artifact should be appended to existing content.
    /// </summary>
    [JsonPropertyName("append")]
    public bool? Append { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the last chunk of the artifact.
    /// </summary>
    [JsonPropertyName("lastChunk")]
    public bool? LastChunk { get; set; }
}
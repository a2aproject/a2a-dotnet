using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Sent by server during sendStream or subscribe requests.
/// </summary>
public sealed class TaskArtifactUpdateEvent : TaskUpdateEvent
{
    /// <summary>
    /// Generated artifact.
    /// </summary>
    [JsonPropertyName("artifact")]
    public Artifact Artifact { get; set; } = new Artifact();

    /// <summary>
    /// Indicates if this artifact appends to a previous one.
    /// </summary>
    [JsonPropertyName("append")]
    public bool? Append { get; set; }

    /// <summary>
    /// Indicates if this is the last chunk of the artifact.
    /// </summary>
    [JsonPropertyName("lastChunk")]
    public bool? LastChunk { get; set; }
}
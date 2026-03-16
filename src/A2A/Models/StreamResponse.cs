namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Identifies which payload field is set on a StreamResponse.</summary>
public enum StreamResponseCase
{
    /// <summary>No payload field is set.</summary>
    None,
    /// <summary>Task payload.</summary>
    Task,
    /// <summary>Message payload.</summary>
    Message,
    /// <summary>Task status update event.</summary>
    StatusUpdate,
    /// <summary>Task artifact update event.</summary>
    ArtifactUpdate,
}

/// <summary>Represents a streaming response event. Uses field-presence to indicate the event type.</summary>
public sealed class StreamResponse
{
    /// <summary>Gets or sets the task result.</summary>
    [JsonPropertyName("task")]
    public AgentTask? Task { get; set; }

    /// <summary>Gets or sets the message result.</summary>
    [JsonPropertyName("message")]
    public Message? Message { get; set; }

    /// <summary>Gets or sets the task status update event.</summary>
    [JsonPropertyName("statusUpdate")]
    public TaskStatusUpdateEvent? StatusUpdate { get; set; }

    /// <summary>Gets or sets the task artifact update event.</summary>
    [JsonPropertyName("artifactUpdate")]
    public TaskArtifactUpdateEvent? ArtifactUpdate { get; set; }

    /// <summary>Gets which payload field is currently set.</summary>
    [JsonIgnore]
    public StreamResponseCase PayloadCase =>
        Task is not null ? StreamResponseCase.Task :
        Message is not null ? StreamResponseCase.Message :
        StatusUpdate is not null ? StreamResponseCase.StatusUpdate :
        ArtifactUpdate is not null ? StreamResponseCase.ArtifactUpdate :
        StreamResponseCase.None;
}

namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents the status of a task in the A2A protocol.</summary>
public sealed class TaskStatus
{
    /// <summary>Gets or sets the state of the task.</summary>
    [JsonPropertyName("state"), JsonRequired]
    public TaskState State { get; set; }

    /// <summary>Gets or sets the message associated with this status.</summary>
    [JsonPropertyName("message")]
    public Message? Message { get; set; }

    /// <summary>Gets or sets the timestamp of this status.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; set; }
}

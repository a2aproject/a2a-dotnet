using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents the status information for an agent task.
/// </summary>
public class AgentTaskStatus
{
    /// <summary>
    /// Gets or sets the current state of the task.
    /// </summary>
    [JsonPropertyName("state")]
    [JsonRequired]
    public TaskState State { get; set; }

    /// <summary>
    /// Gets or sets an optional message associated with the current status.
    /// </summary>
    [JsonPropertyName("message")]
    public Message? Message { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this status was set.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
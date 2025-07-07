using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// TaskState and accompanying message.
/// </summary>
public class AgentTaskStatus
{
    /// <summary>
    /// The current state of the task
    /// </summary>
    [JsonPropertyName("state")]
    [JsonRequired]
    public TaskState State { get; set; }

    /// <summary>
    /// Additional status updates for client
    /// </summary>
    [JsonPropertyName("message")]
    public Message? Message { get; set; }

    /// <summary>
    /// ISO 8601 datetime string when the status was recorded.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
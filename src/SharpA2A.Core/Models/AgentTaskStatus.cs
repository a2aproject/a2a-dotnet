using System.Text.Json.Serialization;

namespace SharpA2A.Core;

public class AgentTaskStatus
{
    [JsonPropertyName("state")]
    [JsonRequired]
    public TaskState State { get; set; }

    [JsonPropertyName("message")]
    public Message? Message { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}



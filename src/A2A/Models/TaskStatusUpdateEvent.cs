using System.Text.Json.Serialization;

namespace A2A;

public class TaskStatusUpdateEvent : TaskUpdateEvent
{
    [JsonPropertyName("status")]
    public AgentTaskStatus Status { get; set; } = new AgentTaskStatus();

    [JsonPropertyName("final")]
    public bool Final { get; set; } = false;
}



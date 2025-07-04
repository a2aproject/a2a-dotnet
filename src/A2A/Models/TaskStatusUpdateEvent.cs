using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents an event that occurs when a task's status is updated.
/// </summary>
public class TaskStatusUpdateEvent : TaskUpdateEvent
{
    /// <summary>
    /// Gets or sets the updated status of the task.
    /// </summary>
    [JsonPropertyName("status")]
    public AgentTaskStatus Status { get; set; } = new AgentTaskStatus();

    /// <summary>
    /// Gets or sets a value indicating whether this is the final status update for the task.
    /// </summary>
    [JsonPropertyName("final")]
    public bool Final { get; set; } = false;
}
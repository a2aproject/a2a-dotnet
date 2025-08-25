using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Event sent by server during sendStream or subscribe requests.
/// </summary>
public sealed class TaskStatusUpdateEvent() : TaskUpdateEvent(KindValue)
{
    internal const string KindValue = "status-update";

    /// <summary>
    /// Gets or sets the current status of the task.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonRequired]
    public AgentTaskStatus Status { get; set; } = new AgentTaskStatus();

    /// <summary>
    /// Gets or sets a value indicating whether this indicates the end of the event stream.
    /// </summary>
    [JsonPropertyName("final")]
    public bool Final { get; set; } = false;
}
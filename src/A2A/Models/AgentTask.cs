using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a task that can be processed by an agent.
/// </summary>
public sealed class AgentTask : A2AResponse
{
    /// <summary>
    /// Unique identifier for the task.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Server-generated id for contextual alignment across interactions.
    /// </summary>
    [JsonPropertyName("contextId")]
    [JsonRequired]
    public string ContextId { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the task.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonRequired]
    public AgentTaskStatus Status { get; set; } = new AgentTaskStatus();

    /// <summary>
    /// Collection of artifacts created by the agent.
    /// </summary>
    [JsonPropertyName("artifacts")]
    public List<Artifact>? Artifacts { get; set; }

    /// <summary>
    /// Collection of messages in the task history.
    /// </summary>
    [JsonPropertyName("history")]
    public List<Message>? History { get; set; } = [];

    /// <summary>
    /// Extension metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

/// <summary>
/// Provides extension methods for <see cref="AgentTask"/>.
/// </summary>
public static class AgentTaskExtensions
{
    /// <summary>
    /// Returns a new <see cref="AgentTask"/> with the <see cref="AgentTask.History"/> collection trimmed to the specified length.
    /// </summary>
    /// <param name="task">The <see cref="AgentTask"/> whose history should be trimmed.</param>
    /// <param name="toLength">The maximum number of messages to retain in the history. If <c>null</c> or greater than the current count, the original task is returned.</param>
    /// <returns>A new <see cref="AgentTask"/> with the history trimmed, or the original task if no trimming is necessary.</returns>
    public static AgentTask WithHistoryTrimmedTo(this AgentTask task, int? toLength)
    {
        if (toLength is not { } len || task.History is not { Count: > 0 } history || history.Count <= len)
        {
            return task;
        }

        return new AgentTask
        {
            Id = task.Id,
            ContextId = task.ContextId,
            Status = task.Status,
            Artifacts = task.Artifacts,
            Metadata = task.Metadata,
            History = [.. history.Skip(history.Count - len)],
        };
    }
}
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a task that can be processed by an agent.
/// </summary>
public class AgentTask : A2AResponse
{
    /// <summary>
    /// Unique identifier for the task.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonRequired]
    public virtual string Id { get; set; } = string.Empty;

    /// <summary>
    /// Server-generated id for contextual alignment across interactions.
    /// </summary>
    [JsonPropertyName("contextId")]
    [JsonRequired]
    public virtual string ContextId { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the task.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonRequired]
    public virtual AgentTaskStatus Status { get; set; } = new AgentTaskStatus();

    /// <summary>
    /// Collection of artifacts created by the agent.
    /// </summary>
    [JsonPropertyName("artifacts")]
    public virtual List<Artifact>? Artifacts { get; set; }

    /// <summary>
    /// Collection of messages in the task history.
    /// </summary>
    [JsonPropertyName("history")]
    public virtual List<Message>? History { get; set; } = [];

    /// <summary>
    /// Extension metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public virtual Dictionary<string, JsonElement>? Metadata { get; set; }
}

internal sealed class TrimmedAgentTask(AgentTask taskToTrim, int? historyLength) : AgentTask
{
    public override List<Artifact>? Artifacts { get => taskToTrim.Artifacts; set => taskToTrim.Artifacts = value; }
    public override string ContextId { get => taskToTrim.ContextId; set => taskToTrim.ContextId = value; }
    public override List<Message>? History { get => (historyLength is { } len && taskToTrim.History?.Count is { } c && c > len) ? [.. taskToTrim.History!.Skip(Math.Max(0, c - len))] : taskToTrim.History; set => taskToTrim.History = value; }
    public override string Id { get => taskToTrim.Id; set => taskToTrim.Id = value; }
    public override Dictionary<string, JsonElement>? Metadata { get => taskToTrim.Metadata; set => taskToTrim.Metadata = value; }
    public override AgentTaskStatus Status { get => taskToTrim.Status; set => taskToTrim.Status = value; }
}

/// <summary>
/// Provides extension methods for <see cref="AgentTask"/>.
/// </summary>
public static class AgentTaskExtensions
{
    /// <summary>
    /// Trims the <see cref="AgentTask.History"/> collection to the specified length, keeping only the most recent messages.
    /// </summary>
    /// <param name="task">
    /// The <see cref="AgentTask"/> whose history should be trimmed.
    /// </param>
    /// <param name="toLength">
    /// The maximum number of messages to retain in the history. If <c>null</c> or greater than the current count, no trimming occurs.
    /// </param>
    /// <returns>
    /// An <see cref="AgentTask"/> with the history trimmed to the specified length.
    /// </returns>
    public static AgentTask WithHistoryTrimmedTo(this AgentTask task, int? toLength) => new TrimmedAgentTask(task, toLength);
}
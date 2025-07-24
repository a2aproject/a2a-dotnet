namespace A2A.Extensions;

/// <summary>
/// Provides extension methods for <see cref="AgentTask"/>.
/// </summary>
internal static class AgentTaskExtensions
{
    /// <summary>
    /// Returns a new <see cref="AgentTask"/> with the <see cref="AgentTask.History"/> collection trimmed to the specified length.
    /// </summary>
    /// <param name="task">The <see cref="AgentTask"/> whose history should be trimmed.</param>
    /// <param name="toLength">The maximum number of messages to retain in the history. If <c>null</c> or greater than the current count, the original task is returned.</param>
    /// <returns>A new <see cref="AgentTask"/> with the history trimmed, or the original task if no trimming is necessary.</returns>
    public static AgentTask WithHistoryTrimmedTo(this AgentTask task, int? toLength)
    {
        if (toLength is not { } len || len < 0 || task.History is not { Count: > 0 } history || history.Count <= len)
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
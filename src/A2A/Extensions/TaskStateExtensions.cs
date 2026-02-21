namespace A2A;

/// <summary>
/// Extension methods for <see cref="TaskState"/>.
/// </summary>
internal static class TaskStateExtensions
{
    /// <summary>
    /// Returns true if the state is terminal (completed, failed, canceled, rejected).
    /// Terminal tasks cannot accept further messages per spec §3.1.1.
    /// </summary>
    /// <param name="state">The task state to check.</param>
    internal static bool IsTerminal(this TaskState state) =>
        state is TaskState.Completed
            or TaskState.Failed
            or TaskState.Canceled
            or TaskState.Rejected;
}

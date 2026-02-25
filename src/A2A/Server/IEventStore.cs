namespace A2A;

/// <summary>
/// Append-only event store for A2A task events.
/// Events are partitioned per task ID with per-task sequence numbers.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Append an event to a task's event log.
    /// </summary>
    /// <param name="taskId">The task to append to.</param>
    /// <param name="streamEvent">The event to store.</param>
    /// <param name="expectedVersion">
    /// If non-null, the append succeeds only if the current log length matches.
    /// Enables optimistic concurrency when needed.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The 0-based sequence number assigned to this event.</returns>
    Task<long> AppendAsync(
        string taskId,
        StreamResponse streamEvent,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read events for a task starting from a given position.
    /// </summary>
    /// <param name="taskId">The task to read.</param>
    /// <param name="fromVersion">0-based position to start reading from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ordered sequence of events with their version numbers.</returns>
    IAsyncEnumerable<EventEnvelope> ReadAsync(
        string taskId,
        long fromVersion = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check whether a task exists (has any events).
    /// </summary>
    /// <param name="taskId">The task to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> ExistsAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the latest version number for a task's event log.
    /// </summary>
    /// <param name="taskId">The task to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest 0-based version, or -1 if no events exist.</returns>
    Task<long> GetLatestVersionAsync(string taskId, CancellationToken cancellationToken = default);
}

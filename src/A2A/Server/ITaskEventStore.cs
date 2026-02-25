namespace A2A;

/// <summary>
/// Combined event store and query interface for A2A tasks.
/// The event log is the source of truth; queries operate on projected state.
/// </summary>
/// <remarks>
/// <para>Production implementations should maintain a <b>materialized projection</b>
/// (e.g., a <c>task_projections</c> table) alongside the event log. Update it
/// transactionally inside <see cref="IEventStore.AppendAsync"/> using
/// <see cref="TaskProjection.Apply"/> to fold each new event into the current state.</para>
/// <para><see cref="GetTaskAsync"/> and <see cref="ListTasksAsync"/> should read from
/// the projection — not replay events — to enable O(1) lookups and indexed
/// filtering/pagination by tenant, context, status, and timestamp.</para>
/// <para>The built-in <see cref="InMemoryEventStore"/> uses an inline projection cache
/// suitable for development and testing. For persistent stores, replaying all tasks
/// into memory on startup is impractical at scale.</para>
/// </remarks>
public interface ITaskEventStore : IEventStore
{
    /// <summary>
    /// Project the current state of a task by replaying its event log.
    /// </summary>
    /// <param name="taskId">The task to project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The projected task, or null if no events exist for the task ID.</returns>
    Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Query tasks with filtering and pagination.
    /// Operates over projected task state.
    /// </summary>
    /// <param name="request">The query request with filters and pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default);
}

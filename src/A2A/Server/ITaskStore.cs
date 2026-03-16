namespace A2A;

/// <summary>
/// Persistence interface for A2A task state.
/// Implementations provide durable storage for <see cref="AgentTask"/> objects.
/// </summary>
/// <remarks>
/// <para>The SDK manages task state mutations internally using
/// <see cref="TaskProjection.Apply"/> before calling <see cref="SaveTaskAsync"/>.
/// Implementations only need to persist and retrieve the fully-formed task object.</para>
/// <para>For live event streaming (<c>SubscribeToTask</c>, <c>SendStreamingMessage</c>),
/// the SDK uses <see cref="ChannelEventNotifier"/> independently of this interface.
/// Implementations do not need to handle event notification or pub/sub.</para>
/// </remarks>
public interface ITaskStore
{
    /// <summary>Get the current state of a task.</summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>Save (upsert) the current state of a task.</summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="task">The task to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default);

    /// <summary>Delete a task.</summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// This method is not called by the SDK itself. It is provided for implementations
    /// that need task pruning (e.g. TTL expiry, admin cleanup). Implementations that
    /// do not need deletion may leave the body empty or throw <see cref="NotSupportedException"/>.
    /// </remarks>
    Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Query tasks with filtering and pagination.
    /// Supports filtering by <see cref="ListTasksRequest.ContextId"/>,
    /// <see cref="ListTasksRequest.Status"/>, <see cref="ListTasksRequest.StatusTimestampAfter"/>,
    /// and cursor-based pagination.
    /// </summary>
    /// <param name="request">The query request with filters and pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default);
}

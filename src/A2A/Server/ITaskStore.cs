namespace A2A;

/// <summary>Defines the interface for persisting A2A tasks.</summary>
public interface ITaskStore
{
    /// <summary>Gets a task by its identifier.</summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The task if found; otherwise, <see langword="null"/>.</returns>
    Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates a task.</summary>
    /// <param name="task">The task to create or update.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The persisted task.</returns>
    Task<AgentTask> SetTaskAsync(AgentTask task, CancellationToken cancellationToken = default);

    /// <summary>Updates the status of a task.</summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="status">The new status.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The updated task.</returns>
    Task<AgentTask> UpdateStatusAsync(string taskId, TaskStatus status, CancellationToken cancellationToken = default);

    /// <summary>Appends a message to the task history.</summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="message">The message to append.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The updated task.</returns>
    Task<AgentTask> AppendHistoryAsync(string taskId, Message message, CancellationToken cancellationToken = default);

    /// <summary>Lists tasks with optional filtering and pagination.</summary>
    /// <param name="request">The list tasks request parameters.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A paginated list of tasks.</returns>
    Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default);
}
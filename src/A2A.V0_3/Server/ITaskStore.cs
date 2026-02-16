namespace A2A.V0_3;

/// <summary>
/// Interface for storing and retrieving agent tasks.
/// </summary>
public interface ITaskStore
{
    /// <summary>
    /// Retrieves a task by its ID.
    /// </summary>
    /// <param name="taskId">The ID of the task to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The task if found, null otherwise.</returns>
    Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves push notification configuration for a task.
    /// </summary>
    /// <param name="taskId">The ID of the task.</param>
    /// <param name="notificationConfigId">The ID of the push notification configuration.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The push notification configuration if found, null otherwise.</returns>
    Task<TaskPushNotificationConfig?> GetPushNotificationAsync(string taskId, string notificationConfigId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the status of a task.
    /// </summary>
    /// <param name="taskId">The ID of the task.</param>
    /// <param name="status">The new status.</param>
    /// <param name="message">Optional message associated with the status.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The updated task status.</returns>
    Task<AgentTaskStatus> UpdateStatusAsync(string taskId, TaskState status, AgentMessage? message = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores or updates a task.
    /// </summary>
    /// <param name="task">The task to store.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task representing the operation.</returns>
    Task SetTaskAsync(AgentTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores push notification configuration for a task.
    /// </summary>
    /// <param name="pushNotificationConfig">The push notification configuration.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task representing the operation.</returns>
    Task SetPushNotificationConfigAsync(TaskPushNotificationConfig pushNotificationConfig, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves push notifications for a task.
    /// </summary>
    /// <param name="taskId">The ID of the task.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A list of push notification configurations for the task.</returns>
    Task<IEnumerable<TaskPushNotificationConfig>> GetPushNotificationsAsync(string taskId, CancellationToken cancellationToken = default);
}
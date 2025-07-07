namespace A2A;

/// <summary>
/// Interface for storing and retrieving agent tasks.
/// </summary>
public interface ITaskStore
{
    /// <summary>
    /// Retrieves a task by its ID.
    /// </summary>
    /// <param name="taskId">The ID of the task to retrieve.</param>
    /// <returns>The task if found, null otherwise</returns>
    Task<AgentTask?> GetTaskAsync(string taskId);

    /// <summary>
    /// Retrieves push notification configuration for a task.
    /// </summary>
    /// <param name="taskId">The ID of the task.</param>
    /// <returns>The push notification configuration if found, null otherwise</returns>
    Task<TaskPushNotificationConfig?> GetPushNotificationAsync(string taskId);

    /// <summary>
    /// Updates the status of a task.
    /// </summary>
    /// <param name="taskId">The ID of the task.</param>
    /// <param name="status">The new status.</param>
    /// <param name="message">Optional message associated with the status.</param>
    /// <returns>The updated task status</returns>
    Task<AgentTaskStatus> UpdateStatusAsync(string taskId, TaskState status, Message? message = null);

    /// <summary>
    /// Stores or updates a task.
    /// </summary>
    /// <param name="task">The task to store.</param>
    /// <returns>A task representing the operation</returns>
    Task SetTaskAsync(AgentTask task);

    /// <summary>
    /// Stores push notification configuration for a task.
    /// </summary>
    /// <param name="pushNotificationConfig">The push notification configuration.</param>
    /// <returns>A task representing the operation</returns>
    Task SetPushNotificationConfigAsync(TaskPushNotificationConfig pushNotificationConfig);
}
namespace A2A;

/// <summary>
/// Defines the contract for storing and retrieving agent tasks and related configurations.
/// </summary>
public interface ITaskStore
{
    /// <summary>
    /// Retrieves a specific agent task by its unique identifier.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task to retrieve.</param>
    /// <returns>A task that represents the asynchronous operation, containing the agent task if found, or null if not found.</returns>
    Task<AgentTask?> GetTaskAsync(string taskId);

    /// <summary>
    /// Retrieves the push notification configuration for a specific task.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task.</param>
    /// <returns>A task that represents the asynchronous operation, containing the push notification configuration if found, or null if not found.</returns>
    Task<TaskPushNotificationConfig?> GetPushNotificationAsync(string taskId);

    /// <summary>
    /// Updates the status of a specific task.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task to update.</param>
    /// <param name="status">The new task state to set.</param>
    /// <param name="message">An optional message to include with the status update.</param>
    /// <returns>A task that represents the asynchronous operation, containing the updated task status.</returns>
    Task<AgentTaskStatus> UpdateStatusAsync(string taskId, TaskState status, Message? message = null);

    /// <summary>
    /// Stores or updates an agent task in the storage.
    /// </summary>
    /// <param name="task">The agent task to store or update.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SetTaskAsync(AgentTask task);

    /// <summary>
    /// Stores or updates the push notification configuration for a task.
    /// </summary>
    /// <param name="pushNotificationConfig">The push notification configuration to store.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SetPushNotificationConfigAsync(TaskPushNotificationConfig pushNotificationConfig);
}
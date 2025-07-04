using System.Net.ServerSentEvents;

namespace A2A;

/// <summary>
/// Defines the contract for an A2A (Agent-to-Agent) client that facilitates communication with agents.
/// </summary>
public interface IA2AClient
{
    /// <summary>
    /// Sends a message to an agent asynchronously and receives a response.
    /// </summary>
    /// <param name="taskSendParams">The parameters containing the message to send.</param>
    /// <returns>A task that represents the asynchronous operation, containing the agent's response.</returns>
    Task<A2AResponse> SendMessageAsync(MessageSendParams taskSendParams);

    /// <summary>
    /// Retrieves a specific task by its unique identifier.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task to retrieve.</param>
    /// <returns>A task that represents the asynchronous operation, containing the agent task details.</returns>
    Task<AgentTask> GetTaskAsync(string taskId);

    /// <summary>
    /// Cancels a specific task identified by the provided parameters.
    /// </summary>
    /// <param name="taskIdParams">The parameters containing the task identifier to cancel.</param>
    /// <returns>A task that represents the asynchronous operation, containing the cancelled task details.</returns>
    Task<AgentTask> CancelTaskAsync(TaskIdParams taskIdParams);

    /// <summary>
    /// Sends a message to an agent and receives a stream of real-time events as Server-Sent Events.
    /// </summary>
    /// <param name="taskSendParams">The parameters containing the message to send.</param>
    /// <returns>An asynchronous enumerable of Server-Sent Events containing A2A events.</returns>
    IAsyncEnumerable<SseItem<A2AEvent>> SendMessageStreamAsync(MessageSendParams taskSendParams);

    /// <summary>
    /// Resubscribes to an existing task to receive real-time updates as Server-Sent Events.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task to resubscribe to.</param>
    /// <returns>An asynchronous enumerable of Server-Sent Events containing A2A events.</returns>
    IAsyncEnumerable<SseItem<A2AEvent>> ResubscribeToTaskAsync(string taskId);

    /// <summary>
    /// Sets push notification configuration for a task.
    /// </summary>
    /// <param name="pushNotificationConfig">The push notification configuration to set.</param>
    /// <returns>A task that represents the asynchronous operation, containing the configured push notification settings.</returns>
    Task<TaskPushNotificationConfig> SetPushNotificationAsync(TaskPushNotificationConfig pushNotificationConfig);

    /// <summary>
    /// Retrieves the push notification configuration for a specific task.
    /// </summary>
    /// <param name="taskIdParams">The parameters containing the task identifier.</param>
    /// <returns>A task that represents the asynchronous operation, containing the push notification configuration.</returns>
    Task<TaskPushNotificationConfig> GetPushNotificationAsync(TaskIdParams taskIdParams);
}

using System.Net.ServerSentEvents;

namespace A2A;

/// <summary>
/// Interface for A2A client operations for interacting with an A2A agent.
/// </summary>
public interface IA2AClient
{
    /// <summary>
    /// Sends a non-streaming message request to the agent.
    /// </summary>
    /// <param name="taskSendParams">The message parameters containing the message and configuration.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The agent's response containing a Task or Message</returns>
    Task<A2AResponse> SendMessageAsync(MessageSendParams taskSendParams, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the current state and history of a specific task.
    /// </summary>
    /// <param name="taskId">The ID of the task to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The requested task with its current state and history</returns>
    Task<AgentTask> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests the agent to cancel a specific task.
    /// </summary>
    /// <param name="taskIdParams">Parameters containing the task ID to cancel.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The updated task with canceled status</returns>
    Task<AgentTask> CancelTaskAsync(TaskIdParams taskIdParams, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a streaming message request to the agent and yields responses as they arrive.
    /// </summary>
    /// <remarks>
    /// This method uses Server-Sent Events (SSE) to receive a stream of updates from the agent.
    /// </remarks>
    /// <param name="taskSendParams">The message parameters containing the message and configuration.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>An async enumerable of server-sent events containing Task, Message, TaskStatusUpdateEvent, or TaskArtifactUpdateEvent</returns>
    IAsyncEnumerable<SseItem<A2AEvent>> SendMessageStreamAsync(MessageSendParams taskSendParams, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resubscribes to a task's event stream to receive ongoing updates.
    /// </summary>
    /// <param name="taskId">The ID of the task to resubscribe to.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>An async enumerable of server-sent events containing task updates</returns>
    IAsyncEnumerable<SseItem<A2AEvent>> ResubscribeToTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or updates the push notification configuration for a specific task.
    /// </summary>
    /// <param name="pushNotificationConfig">The push notification configuration to set.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The configured push notification settings with confirmation</returns>
    Task<TaskPushNotificationConfig> SetPushNotificationAsync(TaskPushNotificationConfig pushNotificationConfig, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the push notification configuration for a specific task.
    /// </summary>
    /// <param name="taskIdParams">Parameters containing the task ID.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The push notification configuration for the specified task</returns>
    Task<TaskPushNotificationConfig> GetPushNotificationAsync(TaskIdParams taskIdParams, CancellationToken cancellationToken = default);
}

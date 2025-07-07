using System.Net.ServerSentEvents;

namespace A2A;

/// <summary>
/// Interface for A2A client operations for interacting with an A2A agent
/// </summary>
public interface IA2AClient
{
    /// <summary>
    /// Sends a non-streaming message request to the agent
    /// </summary>
    /// <param name="taskSendParams">The message parameters containing the message and configuration</param>
    /// <returns>The agent's response containing a Task or Message</returns>
    Task<A2AResponse> SendMessageAsync(MessageSendParams taskSendParams);

    /// <summary>
    /// Retrieves the current state and history of a specific task
    /// </summary>
    /// <param name="taskId">The ID of the task to retrieve</param>
    /// <returns>The requested task with its current state and history</returns>
    /// <exception cref="A2AClientHTTPException">Thrown when an HTTP error occurs during the request</exception>
    /// <exception cref="A2AClientJsonException">Thrown when the response body cannot be decoded as JSON or validated</exception>
    Task<AgentTask> GetTaskAsync(string taskId);

    /// <summary>
    /// Requests the agent to cancel a specific task
    /// </summary>
    /// <param name="taskIdParams">Parameters containing the task ID to cancel</param>
    /// <returns>The updated task with canceled status</returns>
    /// <exception cref="A2AClientHTTPException">Thrown when an HTTP error occurs during the request</exception>
    /// <exception cref="A2AClientJsonException">Thrown when the response body cannot be decoded as JSON or validated</exception>
    Task<AgentTask> CancelTaskAsync(TaskIdParams taskIdParams);

    /// <summary>
    /// Sends a streaming message request to the agent and yields responses as they arrive.
    /// This method uses Server-Sent Events (SSE) to receive a stream of updates from the agent.
    /// </summary>
    /// <param name="taskSendParams">The message parameters containing the message and configuration</param>
    /// <returns>An async enumerable of server-sent events containing Task, Message, TaskStatusUpdateEvent, or TaskArtifactUpdateEvent</returns>
    /// <exception cref="A2AClientHTTPException">Thrown when an HTTP or SSE protocol error occurs during the request</exception>
    /// <exception cref="A2AClientJsonException">Thrown when an SSE event data cannot be decoded as JSON or validated</exception>
    IAsyncEnumerable<SseItem<A2AEvent>> SendMessageStreamAsync(MessageSendParams taskSendParams);

    /// <summary>
    /// Resubscribes to a task's event stream to receive ongoing updates
    /// </summary>
    /// <param name="taskId">The ID of the task to resubscribe to</param>
    /// <returns>An async enumerable of server-sent events containing task updates</returns>
    /// <exception cref="A2AClientHTTPException">Thrown when an HTTP or SSE protocol error occurs during the request</exception>
    /// <exception cref="A2AClientJsonException">Thrown when an SSE event data cannot be decoded as JSON or validated</exception>
    IAsyncEnumerable<SseItem<A2AEvent>> ResubscribeToTaskAsync(string taskId);

    /// <summary>
    /// Sets or updates the push notification configuration for a specific task
    /// </summary>
    /// <param name="pushNotificationConfig">The push notification configuration to set</param>
    /// <returns>The configured push notification settings with confirmation</returns>
    /// <exception cref="A2AClientHTTPException">Thrown when an HTTP error occurs during the request</exception>
    /// <exception cref="A2AClientJsonException">Thrown when the response body cannot be decoded as JSON or validated</exception>
    Task<TaskPushNotificationConfig> SetPushNotificationAsync(TaskPushNotificationConfig pushNotificationConfig);

    /// <summary>
    /// Retrieves the push notification configuration for a specific task
    /// </summary>
    /// <param name="taskIdParams">Parameters containing the task ID</param>
    /// <returns>The push notification configuration for the specified task</returns>
    /// <exception cref="A2AClientHTTPException">Thrown when an HTTP error occurs during the request</exception>
    /// <exception cref="A2AClientJsonException">Thrown when the response body cannot be decoded as JSON or validated</exception>
    Task<TaskPushNotificationConfig> GetPushNotificationAsync(TaskIdParams taskIdParams);
}

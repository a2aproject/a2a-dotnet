namespace A2A;

/// <summary>
/// Interface for managing agent tasks and their lifecycle.
/// </summary>
/// <remarks>
/// Responsible for retrieving, saving, and updating Task objects based on events received from the agent.
/// </remarks>
public interface ITaskManager
{
    /// <summary>
    /// Gets or sets the handler for when a message is received.
    /// </summary>
    /// <remarks>
    /// Used when the task is configured to process simple messages without tasks.
    /// </remarks>
    Func<MessageSendParams, Task<Message>>? OnMessageReceived { get; set; }

    /// <summary>
    /// Gets or sets the handler for when a task is created.
    /// </summary>
    /// <remarks>
    /// Called after a new task object is created and persisted.
    /// </remarks>
    Func<AgentTask, Task> OnTaskCreated { get; set; }

    /// <summary>
    /// Gets or sets the handler for when a task is cancelled.
    /// </summary>
    /// <remarks>
    /// Called after a task's status is updated to Canceled.
    /// </remarks>
    Func<AgentTask, Task> OnTaskCancelled { get; set; }

    /// <summary>
    /// Gets or sets the handler for when a task is updated.
    /// </summary>
    /// <remarks>
    /// Called after an existing task's history or status is modified.
    /// </remarks>
    Func<AgentTask, Task> OnTaskUpdated { get; set; }

    /// <summary>
    /// Gets or sets the handler for when an agent card is queried.
    /// </summary>
    /// <remarks>
    /// Returns agent capability information for a given agent URL.
    /// </remarks>
    Func<string, AgentCard> OnAgentCardQuery { get; set; }

    /// <summary>
    /// Creates a new agent task with a unique ID and initial status.
    /// </summary>
    /// <remarks>
    /// The task is immediately persisted to the task store.
    /// </remarks>
    /// <param name="contextId">
    /// Optional context ID for the task. If null, a new GUID is generated.
    /// </param>
    /// <param name="taskId">
    /// Optional task ID for the task. If null, a new GUID is generated.
    /// </param>
    /// <returns>
    /// The created <see cref="AgentTask"/> with <see cref="TaskState.Submitted"/> status and unique identifiers.
    /// </returns>
    Task<AgentTask> CreateTaskAsync(string? contextId = null, string? taskId = null);

    /// <summary>
    /// Adds an artifact to a task and notifies any active event streams.
    /// </summary>
    /// <remarks>
    /// The artifact is appended to the task's artifacts collection and persisted.
    /// </remarks>
    /// <param name="taskId">The ID of the task to add the artifact to.</param>
    /// <param name="artifact">The artifact to add to the task.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ReturnArtifactAsync(string taskId, Artifact artifact);

    /// <summary>
    /// Updates the status of a task and optionally adds a message to its history.
    /// </summary>
    /// <remarks>
    /// Notifies any active event streams about the status change.
    /// </remarks>
    /// <param name="taskId">The ID of the task to update.</param>
    /// <param name="status">The new task status to set.</param>
    /// <param name="message">Optional message to add to the task history along with the status update.</param>
    /// <param name="final">Whether this is a final status update that should close any active streams.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateStatusAsync(string taskId, TaskState status, Message? message = null, bool final = false);

    /// <summary>
    /// Cancels a task by setting its status to Canceled and invoking the cancellation handler.
    /// </summary>
    /// <remarks>
    /// Retrieves the task from the store, updates its status, and notifies the cancellation handler.
    /// </remarks>
    /// <param name="taskIdParams">Parameters containing the task ID to cancel.</param>
    /// <returns>The canceled task with updated status, or null if not found.</returns>
    Task<AgentTask?> CancelTaskAsync(TaskIdParams taskIdParams);

    /// <summary>
    /// Retrieves a task by its ID from the task store.
    /// </summary>
    /// <remarks>
    /// Looks up the task in the persistent store and returns the current state and history.
    /// </remarks>
    /// <param name="taskIdParams">Parameters containing the task ID to retrieve.</param>
    /// <returns>The task if found in the store, null otherwise.</returns>
    Task<AgentTask?> GetTaskAsync(TaskQueryParams taskIdParams);

    /// <summary>
    /// Processes a message request and returns a response, either from an existing task or by creating a new one.
    /// </summary>
    /// <remarks>
    /// If the message contains a task ID, it updates the existing task's history. If no task ID is provided,
    /// it either delegates to the OnMessageReceived handler or creates a new task.
    /// </remarks>
    /// <param name="messageSendParams">The message parameters containing the message content and optional task/context IDs.</param>
    /// <returns>The agent's response as either a Task object or a direct Message from the handler.</returns>
    Task<A2AResponse?> SendMessageAsync(MessageSendParams messageSendParams);

    /// <summary>
    /// Processes a message request and returns a stream of events as they occur.
    /// </summary>
    /// <remarks>
    /// Creates or updates a task and establishes an event stream that yields Task, Message,
    /// TaskStatusUpdateEvent, and TaskArtifactUpdateEvent objects as they are generated.
    /// </remarks>
    /// <param name="messageSendParams">The message parameters containing the message content and optional task/context IDs.</param>
    /// <returns>An async enumerable that yields events as they are produced by the agent.</returns>
    Task<IAsyncEnumerable<A2AEvent>> SendMessageStreamAsync(MessageSendParams messageSendParams);

    /// <summary>
    /// Resubscribes to an existing task's event stream to receive ongoing updates.
    /// </summary>
    /// <remarks>
    /// Returns the event enumerator that was previously established for the task,
    /// allowing clients to reconnect to an active task stream.
    /// </remarks>
    /// <param name="taskIdParams">Parameters containing the task ID to resubscribe to.</param>
    /// <returns>An async enumerable of events for the specified task.</returns>
    IAsyncEnumerable<A2AEvent> SubscribeToTaskAsync(TaskIdParams taskIdParams);

    /// <summary>
    /// Sets or updates the push notification configuration for a specific task.
    /// </summary>
    /// <remarks>
    /// Configures callback URLs and authentication for receiving task updates via HTTP notifications.
    /// </remarks>
    /// <param name="pushNotificationConfig">The push notification configuration containing callback URL and authentication details.</param>
    /// <returns>The configured push notification settings with confirmation.</returns>
    Task<TaskPushNotificationConfig?> SetPushNotificationAsync(TaskPushNotificationConfig pushNotificationConfig);

    /// <summary>
    /// Retrieves the push notification configuration for a specific task.
    /// </summary>
    /// <remarks>
    /// Returns the callback URL and authentication settings configured for receiving task update notifications.
    /// </remarks>
    /// <param name="notificationConfigParams">Parameters containing the task ID and optional push notification config ID.</param>
    /// <returns>The push notification configuration if found, null otherwise.</returns>
    Task<TaskPushNotificationConfig?> GetPushNotificationAsync(GetTaskPushNotificationConfigParams? notificationConfigParams);
}

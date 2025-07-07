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
    /// <param name="contextId">Optional context ID for the task. If null, a new GUID is generated.</param>
    /// <returns>The created task with Submitted status and unique identifiers</returns>
    Task<AgentTask> CreateTaskAsync(string? contextId = null);

    /// <summary>
    /// Adds an artifact to a task and notifies any active event streams.
    /// </summary>
    /// <remarks>
    /// The artifact is appended to the task's artifacts collection and persisted.
    /// </remarks>
    /// <param name="taskId">The ID of the task to add the artifact to.</param>
    /// <param name="artifact">The artifact to add to the task.</param>
    /// <returns>A task representing the asynchronous operation</returns>
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
    /// <returns>A task representing the asynchronous operation</returns>
    Task UpdateStatusAsync(string taskId, TaskState status, Message? message = null, bool final = false);
}

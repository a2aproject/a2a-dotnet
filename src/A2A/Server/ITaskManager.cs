namespace A2A;

/// <summary>
/// Interface for managing agent tasks and their lifecycle.
/// Responsible for retrieving, saving, and updating Task objects based on events received from the agent.
/// </summary>
public interface ITaskManager
{
    /// <summary>
    /// Gets or sets the handler for when a message is received.
    /// Used when the task is configured to process simple messages without tasks.
    /// </summary>
    Func<MessageSendParams, Task<Message>>? OnMessageReceived { get; set; }

    /// <summary>
    /// Gets or sets the handler for when a task is created.
    /// Called after a new task object is created and persisted.
    /// </summary>
    Func<AgentTask, Task> OnTaskCreated { get; set; }

    /// <summary>
    /// Gets or sets the handler for when a task is cancelled.
    /// Called after a task's status is updated to Canceled.
    /// </summary>
    Func<AgentTask, Task> OnTaskCancelled { get; set; }

    /// <summary>
    /// Gets or sets the handler for when a task is updated.
    /// Called after an existing task's history or status is modified.
    /// </summary>
    Func<AgentTask, Task> OnTaskUpdated { get; set; }

    /// <summary>
    /// Gets or sets the handler for when an agent card is queried.
    /// Returns agent capability information for a given agent URL.
    /// </summary>
    Func<string, AgentCard> OnAgentCardQuery { get; set; }

    /// <summary>
    /// Creates a new agent task with a unique ID and initial status.
    /// The task is immediately persisted to the task store.
    /// </summary>
    /// <param name="contextId">Optional context ID for the task. If null, a new GUID is generated</param>
    /// <returns>The created task with Submitted status and unique identifiers</returns>
    Task<AgentTask> CreateTaskAsync(string? contextId = null);

    /// <summary>
    /// Adds an artifact to a task and notifies any active event streams.
    /// The artifact is appended to the task's artifacts collection and persisted.
    /// </summary>
    /// <param name="taskId">The ID of the task to add the artifact to</param>
    /// <param name="artifact">The artifact to add to the task</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task ReturnArtifactAsync(string taskId, Artifact artifact);

    /// <summary>
    /// Updates the status of a task and optionally adds a message to its history.
    /// Notifies any active event streams about the status change.
    /// </summary>
    /// <param name="taskId">The ID of the task to update</param>
    /// <param name="status">The new task status to set</param>
    /// <param name="message">Optional message to add to the task history along with the status update</param>
    /// <param name="final">Whether this is a final status update that should close any active streams</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task UpdateStatusAsync(string taskId, TaskState status, Message? message = null, bool final = false);
}

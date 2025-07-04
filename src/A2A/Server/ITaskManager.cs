namespace A2A;

/// <summary>
/// Defines the contract for managing agent tasks, including task creation, lifecycle management, and event handling.
/// </summary>
public interface ITaskManager
{
    /// <summary>
    /// Gets or sets a function that handles incoming messages without creating a task.
    /// </summary>
    /// <value>A function that processes message send parameters and returns a message response, or null if not set.</value>
    Func<MessageSendParams, Task<Message>>? OnMessageReceived { get; set; }

    /// <summary>
    /// Gets or sets a function that handles task creation events.
    /// </summary>
    /// <value>A function that processes newly created agent tasks.</value>
    Func<AgentTask, Task> OnTaskCreated { get; set; }

    /// <summary>
    /// Gets or sets a function that handles task cancellation events.
    /// </summary>
    /// <value>A function that processes cancelled agent tasks.</value>
    Func<AgentTask, Task> OnTaskCancelled { get; set; }

    /// <summary>
    /// Gets or sets a function that handles task update events.
    /// </summary>
    /// <value>A function that processes updated agent tasks.</value>
    Func<AgentTask, Task> OnTaskUpdated { get; set; }

    /// <summary>
    /// Gets or sets a function that handles agent card queries.
    /// </summary>
    /// <value>A function that returns agent card information based on the agent URL.</value>
    Func<string, AgentCard> OnAgentCardQuery { get; set; }

    /// <summary>
    /// Creates a new agent task with an optional context identifier.
    /// </summary>
    /// <param name="contextId">The optional context identifier to associate with the task. If not provided, a new GUID will be generated.</param>
    /// <returns>A task that represents the asynchronous operation, containing the created agent task.</returns>
    Task<AgentTask> CreateTaskAsync(string? contextId = null);

    /// <summary>
    /// Adds an artifact to a specific task to be returned to the client.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task to add the artifact to.</param>
    /// <param name="artifact">The artifact to add to the task.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ReturnArtifactAsync(string taskId, Artifact artifact);

    /// <summary>
    /// Updates the status of a specific task.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task to update.</param>
    /// <param name="status">The new task state to set.</param>
    /// <param name="message">An optional message to include with the status update.</param>
    /// <param name="final">Indicates whether this is a final status update that completes the task.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task UpdateStatusAsync(string taskId, TaskState status, Message? message = null, bool final = false);
}

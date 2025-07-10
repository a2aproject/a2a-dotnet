using System.Diagnostics;

namespace A2A;

/// <summary>
/// Implementation of task manager for handling agent tasks and their lifecycle.
/// </summary>
/// <remarks>
/// Helps manage a task's lifecycle during execution of a request, responsible for retrieving,
/// saving, and updating the Task object based on events received from the agent.
/// </remarks>
public sealed class TaskManager : ITaskManager
{
    /// <summary>
    /// OpenTelemetry ActivitySource for tracing.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("A2A.TaskManager", "1.0.0");

    private readonly ITaskStore _taskStore;

    private readonly Dictionary<string, TaskUpdateEventEnumerator> _taskUpdateEventEnumerators = [];

    /// <inheritdoc />
    public Func<MessageSendParams, Task<Message>>? OnMessageReceived { get; set; }

    /// <inheritdoc />
    public Func<AgentTask, Task> OnTaskCreated { get; set; } = static _ => Task.CompletedTask;

    /// <inheritdoc />
    public Func<AgentTask, Task> OnTaskCancelled { get; set; } = static _ => Task.CompletedTask;

    /// <inheritdoc />
    public Func<AgentTask, Task> OnTaskUpdated { get; set; } = static _ => Task.CompletedTask;

    /// <inheritdoc />
    public Func<string, AgentCard> OnAgentCardQuery { get; set; } = static agentUrl => new AgentCard() { Name = "Unknown", Url = agentUrl };

    /// <summary>
    /// Initializes a new instance of the TaskManager class.
    /// </summary>
    /// <remarks>
    /// Sets up the task store for persistence and optionally configures HTTP client for callbacks.
    /// </remarks>
    /// <param name="callbackHttpClient">HTTP client for making callback requests to external endpoints (currently unused).</param>
    /// <param name="taskStore">Task store implementation for persisting tasks. If null, defaults to in-memory store.</param>
    public TaskManager(HttpClient? callbackHttpClient = null, ITaskStore? taskStore = null)
    {
        // TODO: Use callbackHttpClient
        _taskStore = taskStore ?? new InMemoryTaskStore();
    }

    /// <inheritdoc />
    public async Task<AgentTask> CreateTaskAsync(string? contextId = null)
    {
        contextId ??= Guid.NewGuid().ToString();

        using var activity = ActivitySource.StartActivity("CreateTask", ActivityKind.Server);
        activity?.SetTag("context.id", contextId);

        // Create a new task with a unique ID and context ID
        var task = new AgentTask
        {
            Id = Guid.NewGuid().ToString(),
            ContextId = contextId,
            Status = new AgentTaskStatus
            {
                State = TaskState.Submitted,
                Timestamp = DateTime.UtcNow
            }
        };
        await _taskStore.SetTaskAsync(task);
        return task;
    }

    /// <summary>
    /// Cancels a task by setting its status to Canceled and invoking the cancellation handler.
    /// </summary>
    /// <remarks>
    /// Retrieves the task from the store, updates its status, and notifies the cancellation handler.
    /// </remarks>
    /// <param name="taskIdParams">Parameters containing the task ID to cancel.</param>
    /// <returns>The canceled task with updated status, or null if not found.</returns>
    public async Task<AgentTask?> CancelTaskAsync(TaskIdParams taskIdParams)
    {
        if (taskIdParams is null)
        {
            throw new ArgumentNullException(nameof(taskIdParams));
        }

        using var activity = ActivitySource.StartActivity("CancelTask", ActivityKind.Server);
        activity?.SetTag("task.id", taskIdParams.Id);

        var task = await _taskStore.GetTaskAsync(taskIdParams.Id);
        if (task != null)
        {
            activity?.SetTag("task.found", true);
            await _taskStore.UpdateStatusAsync(task.Id, TaskState.Canceled);
            await OnTaskCancelled(task);
            return task;
        }

        activity?.SetTag("task.found", false);
        throw new ArgumentException("Task not found or invalid TaskIdParams.");
    }

    /// <summary>
    /// Retrieves a task by its ID from the task store.
    /// </summary>
    /// <remarks>
    /// Looks up the task in the persistent store and returns the current state and history.
    /// </remarks>
    /// <param name="taskIdParams">Parameters containing the task ID to retrieve.</param>
    /// <returns>The task if found in the store, null otherwise.</returns>
    public async Task<AgentTask?> GetTaskAsync(TaskIdParams taskIdParams)
    {
        if (taskIdParams is null)
        {
            throw new ArgumentNullException(nameof(taskIdParams));
        }

        using var activity = ActivitySource.StartActivity("GetTask", ActivityKind.Server);
        activity?.SetTag("task.id", taskIdParams.Id);

        var task = await _taskStore.GetTaskAsync(taskIdParams.Id);
        activity?.SetTag("task.found", task != null);
        return task;
    }

    /// <summary>
    /// Processes a message request and returns a response, either from an existing task or by creating a new one.
    /// </summary>
    /// <remarks>
    /// If the message contains a task ID, it updates the existing task's history. If no task ID is provided,
    /// it either delegates to the OnMessageReceived handler or creates a new task.
    /// </remarks>
    /// <param name="messageSendParams">The message parameters containing the message content and optional task/context IDs.</param>
    /// <returns>The agent's response as either a Task object or a direct Message from the handler.</returns>
    public async Task<A2AResponse?> SendMessageAsync(MessageSendParams messageSendParams)
    {
        if (messageSendParams is null)
        {
            throw new ArgumentNullException(nameof(messageSendParams));
        }

        using var activity = ActivitySource.StartActivity("SendMessage", ActivityKind.Server);

        AgentTask? task = null;
        // Is this message to be associated to an existing Task
        if (messageSendParams.Message.TaskId != null)
        {
            activity?.SetTag("task.id", messageSendParams.Message.TaskId);
            task = await _taskStore.GetTaskAsync(messageSendParams.Message.TaskId);
            if (task == null)
            {
                activity?.SetTag("task.found", false);
                throw new ArgumentException("Task not found or invalid TaskIdParams.");
            }
        }

        if (messageSendParams.Message.ContextId != null)
        {
            activity?.SetTag("task.contextId", messageSendParams.Message.ContextId);
        }

        if (task == null)
        {
            // If the task is configured to process simple messages without tasks, pass the message directly to the agent
            if (OnMessageReceived != null)
            {
                using var createActivity = ActivitySource.StartActivity("OnMessageReceived", ActivityKind.Server);
                return await OnMessageReceived(messageSendParams);
            }
            else
            {
                // If no task is found and no OnMessageReceived handler is set, create a new task
                task = await CreateTaskAsync(messageSendParams.Message.ContextId);
                task.History ??= [];
                task.History.Add(messageSendParams.Message);
                using var createActivity = ActivitySource.StartActivity("OnTaskCreated", ActivityKind.Server);
                await OnTaskCreated(task);
            }
        }
        else
        {
            // Fail if Task is in terminal stateS
            if (task.Status.State is TaskState.Completed or TaskState.Canceled or TaskState.Failed or TaskState.Rejected)
            {
                activity?.SetTag("task.terminalState", true);
                throw new InvalidOperationException("Cannot send message to a task in terminal state.");
            }
            // If the task is found, update its status and history
            task.History ??= [];
            task.History.Add(messageSendParams.Message);
            await _taskStore.SetTaskAsync(task);
            using var createActivity = ActivitySource.StartActivity("OnTaskUpdated", ActivityKind.Server);
            await OnTaskUpdated(task);
        }
        return task;
    }

    /// <summary>
    /// Processes a message request and returns a stream of events as they occur.
    /// </summary>
    /// <remarks>
    /// Creates or updates a task and establishes an event stream that yields Task, Message,
    /// TaskStatusUpdateEvent, and TaskArtifactUpdateEvent objects as they are generated.
    /// </remarks>
    /// <param name="messageSendParams">The message parameters containing the message content and optional task/context IDs.</param>
    /// <returns>An async enumerable that yields events as they are produced by the agent.</returns>
    public async Task<IAsyncEnumerable<A2AEvent>> SendMessageStreamAsync(MessageSendParams messageSendParams)
    {
        if (messageSendParams is null)
        {
            throw new ArgumentNullException(nameof(messageSendParams));
        }

        using var activity = ActivitySource.StartActivity("SendSubscribe", ActivityKind.Server);
        AgentTask? agentTask = null;

        // Is this message to be associated to an existing Task
        if (messageSendParams.Message.TaskId != null)
        {
            activity?.SetTag("task.id", messageSendParams.Message.TaskId);
            agentTask = await _taskStore.GetTaskAsync(messageSendParams.Message.TaskId);
            if (agentTask == null)
            {
                activity?.SetTag("task.found", false);
                throw new ArgumentException("Task not found or invalid TaskIdParams.");
            }
        }

        if (messageSendParams.Message.ContextId != null)
        {
            activity?.SetTag("task.contextId", messageSendParams.Message.ContextId);
        }

        TaskUpdateEventEnumerator enumerator;
        if (agentTask == null)
        {
            // If the task is configured to process simple messages without tasks, pass the message directly to the agent
            if (OnMessageReceived != null)
            {
                var message = await OnMessageReceived(messageSendParams);
                return YieldSingleEvent(message);

                static async IAsyncEnumerable<A2AEvent> YieldSingleEvent(A2AEvent evt)
                {
                    yield return evt;
                    await Task.CompletedTask;
                }
            }
            else
            {
                // If no task is found and no OnMessageReceived handler is set, create a new task
                agentTask = await CreateTaskAsync(messageSendParams.Message.ContextId);
                agentTask.History ??= [];
                agentTask.History.Add(messageSendParams.Message);
                enumerator = new TaskUpdateEventEnumerator();
                _taskUpdateEventEnumerators[agentTask.Id] = enumerator;
                enumerator.NotifyEvent(agentTask);
                enumerator.ProcessingTask = Task.Run(async () =>
                {
                    using var createActivity = ActivitySource.StartActivity("OnTaskCreated", ActivityKind.Server);
                    await OnTaskCreated(agentTask);
                });
            }
        }
        else
        {
            // If the task is found, update its status and history
            agentTask.History ??= [];
            agentTask.History.Add(messageSendParams.Message);
            await _taskStore.SetTaskAsync(agentTask);
            enumerator = new TaskUpdateEventEnumerator();
            _taskUpdateEventEnumerators[agentTask.Id] = enumerator;
            enumerator.ProcessingTask = Task.Run(async () =>
            {
                using var createActivity = ActivitySource.StartActivity("OnTaskUpdated", ActivityKind.Server);
                await OnTaskUpdated(agentTask);
            });
        }

        return enumerator;  //TODO: Clean up enumerators after use
    }

    /// <summary>
    /// Resubscribes to an existing task's event stream to receive ongoing updates.
    /// </summary>
    /// <remarks>
    /// Returns the event enumerator that was previously established for the task,
    /// allowing clients to reconnect to an active task stream.
    /// </remarks>
    /// <param name="taskIdParams">Parameters containing the task ID to resubscribe to.</param>
    /// <returns>An async enumerable of events for the specified task.</returns>
    public IAsyncEnumerable<A2AEvent> ResubscribeAsync(TaskIdParams taskIdParams)
    {
        if (taskIdParams is null)
        {
            throw new ArgumentNullException(nameof(taskIdParams));
        }

        using var activity = ActivitySource.StartActivity("Resubscribe", ActivityKind.Server);
        activity?.SetTag("task.id", taskIdParams.Id);

        return _taskUpdateEventEnumerators.TryGetValue(taskIdParams.Id, out var enumerator) ?
            (IAsyncEnumerable<A2AEvent>)enumerator :
            throw new ArgumentException("Task not found or invalid TaskIdParams.");
    }

    /// <summary>
    /// Sets or updates the push notification configuration for a specific task.
    /// </summary>
    /// <remarks>
    /// Configures callback URLs and authentication for receiving task updates via HTTP notifications.
    /// </remarks>
    /// <param name="pushNotificationConfig">The push notification configuration containing callback URL and authentication details.</param>
    /// <returns>The configured push notification settings with confirmation.</returns>
    public async Task<TaskPushNotificationConfig?> SetPushNotificationAsync(TaskPushNotificationConfig pushNotificationConfig)
    {
        if (pushNotificationConfig is null)
        {
            throw new ArgumentNullException(nameof(pushNotificationConfig));
        }

        await _taskStore.SetPushNotificationConfigAsync(pushNotificationConfig);
        return pushNotificationConfig;
    }

    /// <summary>
    /// Retrieves the push notification configuration for a specific task.
    /// </summary>
    /// <remarks>
    /// Returns the callback URL and authentication settings configured for receiving task update notifications.
    /// </remarks>
    /// <param name="taskIdParams">Parameters containing the task ID to get push notification configuration for.</param>
    /// <returns>The push notification configuration if found, null otherwise.</returns>
    public async Task<TaskPushNotificationConfig?> GetPushNotificationAsync(TaskIdParams taskIdParams)
    {
        if (taskIdParams is null)
        {
            throw new ArgumentNullException(nameof(taskIdParams));
        }

        using var activity = ActivitySource.StartActivity("GetPushNotification", ActivityKind.Server);
        activity?.SetTag("task.id", taskIdParams.Id);

        var pushNotificationConfig = await _taskStore.GetPushNotificationAsync(taskIdParams.Id);
        activity?.SetTag("config.found", pushNotificationConfig != null);
        return pushNotificationConfig;
    }

    /// <inheritdoc />
    public async Task UpdateStatusAsync(string taskId, TaskState status, Message? message = null, bool final = false)
    {
        if (string.IsNullOrEmpty(taskId))
        {
            throw new ArgumentNullException(nameof(taskId));
        }

        using var activity = ActivitySource.StartActivity("UpdateStatus", ActivityKind.Server);
        activity?.SetTag("task.id", taskId);
        activity?.SetTag("task.status", status.ToString());
        activity?.SetTag("task.finalStatus", final);

        try
        {
            var agentStatus = await _taskStore.UpdateStatusAsync(taskId, status, message);
            //TODO: Make callback notification if set by the client
            _taskUpdateEventEnumerators.TryGetValue(taskId, out var enumerator);
            if (enumerator != null)
            {
                var taskUpdateEvent = new TaskStatusUpdateEvent
                {
                    TaskId = taskId,
                    Status = agentStatus,
                    Final = final
                };

                if (final)
                {
                    activity?.SetTag("event.type", "final");
                    enumerator.NotifyFinalEvent(taskUpdateEvent);
                }
                else
                {
                    activity?.SetTag("event.type", "update");
                    enumerator.NotifyEvent(taskUpdateEvent);
                }
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ReturnArtifactAsync(string taskId, Artifact artifact)
    {
        if (string.IsNullOrEmpty(taskId))
        {
            throw new ArgumentNullException(nameof(taskId));
        }
        else if (artifact is null)
        {
            throw new ArgumentNullException(nameof(artifact));
        }

        using var activity = ActivitySource.StartActivity("ReturnArtifact", ActivityKind.Server);
        activity?.SetTag("task.id", taskId);

        try
        {
            var task = await _taskStore.GetTaskAsync(taskId);
            if (task != null)
            {
                activity?.SetTag("task.found", true);

                task.Artifacts ??= [];
                task.Artifacts.Add(artifact);
                await _taskStore.SetTaskAsync(task);

                //TODO: Make callback notification if set by the client
                _taskUpdateEventEnumerators.TryGetValue(task.Id, out var enumerator);
                if (enumerator != null)
                {
                    var taskUpdateEvent = new TaskArtifactUpdateEvent
                    {
                        TaskId = task.Id,
                        Artifact = artifact
                    };
                    activity?.SetTag("event.type", "artifact");
                    enumerator.NotifyEvent(taskUpdateEvent);
                }
            }
            else
            {
                activity?.SetTag("task.found", false);
                activity?.SetStatus(ActivityStatusCode.Error, "Task not found");
                throw new ArgumentException("Task not found.");
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
    // TODO: Implement UpdateArtifact method
}

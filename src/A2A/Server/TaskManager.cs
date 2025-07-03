using System.Diagnostics;

namespace A2A;

public class TaskManager : ITaskManager
{
    // OpenTelemetry ActivitySource
    public static readonly ActivitySource ActivitySource = new("A2A.TaskManager", "1.0.0");

    private readonly ITaskStore _TaskStore;

    public Func<MessageSendParams, Task<Message>>? OnMessageReceived { get; set; }
    /// <summary>
    /// Agent handler for task creation.
    /// </summary>
    public Func<AgentTask, Task> OnTaskCreated { get; set; } = static _ => Task.CompletedTask;

    /// <summary>
    /// Agent handler for task cancellation.
    /// </summary>
    public Func<AgentTask, Task> OnTaskCancelled { get; set; } = static _ => Task.CompletedTask;

    /// <summary>
    /// Agent handler for task update.
    /// </summary>
    public Func<AgentTask, Task> OnTaskUpdated { get; set; } = static _ => Task.CompletedTask;

    /// <summary>
    /// Agent handler for an agent card query.
    /// </summary>
    public Func<string, AgentCard> OnAgentCardQuery { get; set; } = static agentUrl => new AgentCard() { Name = "Unknown", Url = agentUrl };

    private readonly Dictionary<string, TaskUpdateEventEnumerator> _TaskUpdateEventEnumerators = [];

    public TaskManager(HttpClient? callbackHttpClient = null, ITaskStore? taskStore = null)
    {
        // TODO: Use callbackHttpClient
        _TaskStore = taskStore ?? new InMemoryTaskStore();
    }

    public async Task<AgentTask> CreateTaskAsync(string? contextId = null)
    {
        using var activity = ActivitySource.StartActivity("CreateTask", ActivityKind.Server);
        activity?.SetTag("context.id", contextId);

        // Create a new task with a unique ID and context ID
        var task = new AgentTask
        {
            Id = Guid.NewGuid().ToString(),
            ContextId = contextId ?? Guid.NewGuid().ToString(),
            Status = new AgentTaskStatus
            {
                State = TaskState.Submitted,
                Timestamp = DateTime.UtcNow
            }
        };
        await _TaskStore.SetTaskAsync(task);
        return task;
    }

    public async Task<AgentTask?> CancelTaskAsync(TaskIdParams? taskIdParams)
    {
        if (taskIdParams == null)
        {
            throw new ArgumentNullException(nameof(taskIdParams), "TaskIdParams cannot be null.");
        }

        using var activity = ActivitySource.StartActivity("CancelTask", ActivityKind.Server);
        activity?.SetTag("task.id", taskIdParams.Id);

        var task = await _TaskStore.GetTaskAsync(taskIdParams.Id);
        if (task != null)
        {
            activity?.SetTag("task.found", true);
            await _TaskStore.UpdateStatusAsync(task.Id, TaskState.Canceled);
            await OnTaskCancelled(task);
            return task;
        }
        else
        {
            activity?.SetTag("task.found", false);
            throw new ArgumentException("Task not found or invalid TaskIdParams.");
        }
    }
    public async Task<AgentTask?> GetTaskAsync(TaskIdParams? taskIdParams)
    {
        if (taskIdParams == null)
        {
            throw new ArgumentNullException(nameof(taskIdParams), "TaskIdParams cannot be null.");
        }

        using var activity = ActivitySource.StartActivity("GetTask", ActivityKind.Server);
        activity?.SetTag("task.id", taskIdParams.Id);

        var task = await _TaskStore.GetTaskAsync(taskIdParams.Id);
        activity?.SetTag("task.found", task != null);
        return task;
    }
    public async Task<A2AResponse?> SendMessageAsync(MessageSendParams messageSendParams)
    {

        using var activity = ActivitySource.StartActivity("SendMessage", ActivityKind.Server);

        AgentTask? task = null;
        // Is this message to be associated to an existing Task
        if (messageSendParams.Message.TaskId != null)
        {
            activity?.SetTag("task.id", messageSendParams.Message.TaskId);
            task = await _TaskStore.GetTaskAsync(messageSendParams.Message.TaskId);
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
                using var createActivity = ActivitySource.StartActivity("OnMessageReceived", ActivityKind.Server);
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
            await _TaskStore.SetTaskAsync(task);
            using var createActivity = ActivitySource.StartActivity("OnTaskUpdated", ActivityKind.Server);
            await OnTaskUpdated(task);
        }
        return task;
    }

    public async Task<IAsyncEnumerable<A2AEvent>> SendMessageStreamAsync(MessageSendParams messageSendParams)
    {

        using var activity = ActivitySource.StartActivity("SendSubscribe", ActivityKind.Server);
        AgentTask? agentTask = null;

        // Is this message to be associated to an existing Task
        if (messageSendParams.Message.TaskId != null)
        {
            activity?.SetTag("task.id", messageSendParams.Message.TaskId);
            agentTask = await _TaskStore.GetTaskAsync(messageSendParams.Message.TaskId);
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
                _TaskUpdateEventEnumerators[agentTask.Id] = enumerator;
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
            await _TaskStore.SetTaskAsync(agentTask);
            enumerator = new TaskUpdateEventEnumerator();
            _TaskUpdateEventEnumerators[agentTask.Id] = enumerator;
            enumerator.ProcessingTask = Task.Run(async () =>
            {
                using var createActivity = ActivitySource.StartActivity("OnTaskUpdated", ActivityKind.Server);
                await OnTaskUpdated(agentTask);
            });
        }

        return enumerator;  //TODO: Clean up enumerators after use

    }

    public IAsyncEnumerable<A2AEvent> ResubscribeAsync(TaskIdParams? taskIdParams)
    {
        if (taskIdParams == null)
        {
            throw new ArgumentNullException(nameof(taskIdParams), "TaskIdParams cannot be null.");
        }

        using var activity = ActivitySource.StartActivity("Resubscribe", ActivityKind.Server);
        activity?.SetTag("task.id", taskIdParams.Id);

        return _TaskUpdateEventEnumerators.TryGetValue(taskIdParams.Id, out var enumerator) ?
            (IAsyncEnumerable<A2AEvent>)enumerator :
            throw new ArgumentException("Task not found or invalid TaskIdParams.");
    }

    public async Task<TaskPushNotificationConfig?> SetPushNotificationAsync(TaskPushNotificationConfig? pushNotificationConfig)
    {
        if (pushNotificationConfig is null)
        {
            throw new ArgumentException("Missing push notification config.");
        }

        await _TaskStore.SetPushNotificationConfigAsync(pushNotificationConfig);
        return pushNotificationConfig;
    }

    public async Task<TaskPushNotificationConfig?> GetPushNotificationAsync(TaskIdParams? taskIdParams)
    {
        if (taskIdParams == null)
        {
            throw new ArgumentNullException(nameof(taskIdParams), "TaskIdParams cannot be null.");
        }

        using var activity = ActivitySource.StartActivity("GetPushNotification", ActivityKind.Server);
        activity?.SetTag("task.id", taskIdParams.Id);

        var pushNotificationConfig = await _TaskStore.GetPushNotificationAsync(taskIdParams.Id);
        activity?.SetTag("config.found", pushNotificationConfig != null);
        return pushNotificationConfig;
    }

    /// <summary>
    /// Updates the status of a task. This is used by the agent to update the status of a task.
    /// </summary>
    /// <remarks>
    /// Should this be limited to only allow certain state transitions?
    /// </remarks>
    /// <param name="taskId"></param>
    /// <param name="status"></param>
    /// <param name="message"></param>
    /// <returns></returns>    
    public async Task UpdateStatusAsync(string taskId, TaskState status, Message? message = null, bool final = false)
    {
        using var activity = ActivitySource.StartActivity("UpdateStatus", ActivityKind.Server);
        activity?.SetTag("task.id", taskId);
        activity?.SetTag("task.status", status.ToString());
        activity?.SetTag("task.finalStatus", final);

        try
        {
            var agentStatus = await _TaskStore.UpdateStatusAsync(taskId, status, message);
            //TODO: Make callback notification if set by the client
            _TaskUpdateEventEnumerators.TryGetValue(taskId, out var enumerator);
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

    /// <summary>
    /// Enables an agent to add an artifact to a task to be returned to the client.
    /// </summary>
    /// <param name="taskIdParams"></param>
    /// <param name="artifact"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public async Task ReturnArtifactAsync(string taskId, Artifact artifact)
    {
        using var activity = ActivitySource.StartActivity("ReturnArtifact", ActivityKind.Server);
        activity?.SetTag("task.id", taskId);

        try
        {
            var task = await _TaskStore.GetTaskAsync(taskId);
            if (task != null)
            {
                activity?.SetTag("task.found", true);

                task.Artifacts ??= [];
                task.Artifacts.Add(artifact);
                await _TaskStore.SetTaskAsync(task);

                //TODO: Make callback notification if set by the client
                _TaskUpdateEventEnumerators.TryGetValue(task.Id, out var enumerator);
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

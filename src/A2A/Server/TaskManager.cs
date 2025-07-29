using A2A.Extensions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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

    private readonly ConcurrentDictionary<string, TaskUpdateEventEnumerator> _taskUpdateEventEnumerators = [];

    /// <inheritdoc />
    public Func<MessageSendParams, CancellationToken, Task<Message>>? OnMessageReceived { get; set; }

    /// <inheritdoc />
    public Func<AgentTask, CancellationToken, Task> OnTaskCreated { get; set; } = static (_, _) => Task.CompletedTask;

    /// <inheritdoc />
    public Func<AgentTask, CancellationToken, Task> OnTaskCancelled { get; set; } = static (_, _) => Task.CompletedTask;

    /// <inheritdoc />
    public Func<AgentTask, CancellationToken, Task> OnTaskUpdated { get; set; } = static (_, _) => Task.CompletedTask;

    /// <inheritdoc />
    public Func<string, CancellationToken, Task<AgentCard>> OnAgentCardQuery { get; set; }
        = static (agentUrl, ct) => ct.IsCancellationRequested
            ? Task.FromCanceled<AgentCard>(ct)
            : Task.FromResult(new AgentCard() { Name = "Unknown", Url = agentUrl });

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
    public async Task<AgentTask> CreateTaskAsync(string? contextId = null, string? taskId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        contextId ??= Guid.NewGuid().ToString();

        using var activity = ActivitySource.StartActivity("CreateTask", ActivityKind.Server);
        activity?.SetTag("context.id", contextId);

        // Create a new task with a unique ID and context ID
        var task = new AgentTask
        {
            Id = taskId ?? Guid.NewGuid().ToString(),
            ContextId = contextId,
            Status = new AgentTaskStatus
            {
                State = TaskState.Submitted,
                Timestamp = DateTime.UtcNow
            }
        };
        await _taskStore.SetTaskAsync(task, cancellationToken).ConfigureAwait(false);
        return task;
    }

    /// <inheritdoc />
    public async Task<AgentTask?> CancelTaskAsync(TaskIdParams taskIdParams, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (taskIdParams is null)
        {
            throw new ArgumentNullException(nameof(taskIdParams));
        }

        using var activity = ActivitySource.StartActivity("CancelTask", ActivityKind.Server);
        activity?.SetTag("task.id", taskIdParams.Id);

        var task = await _taskStore.GetTaskAsync(taskIdParams.Id, cancellationToken).ConfigureAwait(false);
        if (task is not null)
        {
            activity?.SetTag("task.found", true);

            if (task.Status.State is TaskState.Completed or TaskState.Canceled or TaskState.Failed or TaskState.Rejected)
            {
                // The spec does not specify what to do if the task is already canceled (or other terminal state):
                // https://a2a-protocol.org/latest/specification/#74-taskscancel
                // But the tck tests expect second cancellation to fail:
                // https://github.com/a2aproject/a2a-tck/blob/22f7c191d85f2d4ff2f4564da5d8691944bb7ffd/tests/optional/quality/test_task_state_quality.py#L146
                // But they don't specify the error code, so we throw a generic exception:
                // https://github.com/a2aproject/a2a-tck/blob/22f7c191d85f2d4ff2f4564da5d8691944bb7ffd/tests/optional/quality/test_task_state_quality.py#L180
                throw new A2AException("Task is in a terminal state and cannot be cancelled.", A2AErrorCode.TaskNotCancelable);
            }

            await _taskStore.UpdateStatusAsync(task.Id, TaskState.Canceled, cancellationToken: cancellationToken).ConfigureAwait(false);
            await OnTaskCancelled(task, cancellationToken).ConfigureAwait(false);
            return task;
        }

        activity?.SetTag("task.found", false);
        throw new A2AException("Task not found or invalid TaskIdParams.", A2AErrorCode.TaskNotFound);
    }

    /// <inheritdoc />
    public async Task<AgentTask?> GetTaskAsync(TaskQueryParams taskIdParams, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (taskIdParams is null)
        {
            throw new ArgumentNullException(nameof(taskIdParams));
        }

        using var activity = ActivitySource.StartActivity("GetTask", ActivityKind.Server);
        activity?.SetTag("task.id", taskIdParams.Id);

        var task = await _taskStore.GetTaskAsync(taskIdParams.Id, cancellationToken).ConfigureAwait(false);
        activity?.SetTag("task.found", task != null);

        return task?.WithHistoryTrimmedTo(taskIdParams.HistoryLength);
    }

    /// <inheritdoc />
    public async Task<A2AResponse?> SendMessageAsync(MessageSendParams messageSendParams, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
            task = await _taskStore.GetTaskAsync(messageSendParams.Message.TaskId, cancellationToken).ConfigureAwait(false);
            if (task == null)
            {
                activity?.SetTag("task.found", false);
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
                return await OnMessageReceived(messageSendParams, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // If no task is found and no OnMessageReceived handler is set, create a new task
                task = await CreateTaskAsync(messageSendParams.Message.ContextId, messageSendParams.Message.TaskId, cancellationToken).ConfigureAwait(false);
                task.History ??= [];
                task.History.Add(messageSendParams.Message);
                using var createActivity = ActivitySource.StartActivity("OnTaskCreated", ActivityKind.Server);
                await OnTaskCreated(task, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            // Fail if Task is in terminal states
            if (task.Status.State is TaskState.Completed or TaskState.Canceled or TaskState.Failed or TaskState.Rejected)
            {
                activity?.SetTag("task.terminalState", true);
                throw new InvalidOperationException("Cannot send message to a task in terminal state.");
            }
            // If the task is found, update its status and history
            task.History ??= [];
            task.History.Add(messageSendParams.Message);

            await _taskStore.SetTaskAsync(task, cancellationToken).ConfigureAwait(false);
            using var createActivity = ActivitySource.StartActivity("OnTaskUpdated", ActivityKind.Server);
            await OnTaskUpdated(task, cancellationToken).ConfigureAwait(false);
        }

        return task.WithHistoryTrimmedTo(messageSendParams.Configuration?.HistoryLength);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<A2AEvent> SendMessageStreamAsync(MessageSendParams messageSendParams, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (messageSendParams is null)
        {
            throw new ArgumentNullException(nameof(messageSendParams));
        }

        using var activity = ActivitySource.StartActivity("SendMessageStream", ActivityKind.Server);
        AgentTask? agentTask = null;

        // Is this message to be associated to an existing Task
        if (messageSendParams.Message.TaskId != null)
        {
            activity?.SetTag("task.id", messageSendParams.Message.TaskId);
            agentTask = await _taskStore.GetTaskAsync(messageSendParams.Message.TaskId, cancellationToken).ConfigureAwait(false);
            if (agentTask == null)
            {
                activity?.SetTag("task.found", false);
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
                var message = await OnMessageReceived(messageSendParams, cancellationToken).ConfigureAwait(false);
                yield return message;
                yield break;
            }
            else
            {
                // If no task is found and no OnMessageReceived handler is set, create a new task
                agentTask = await CreateTaskAsync(messageSendParams.Message.ContextId, cancellationToken: cancellationToken).ConfigureAwait(false);
                agentTask.History ??= [];
                agentTask.History.Add(messageSendParams.Message);
                enumerator = new TaskUpdateEventEnumerator();
                _taskUpdateEventEnumerators[agentTask.Id] = enumerator;
                enumerator.NotifyEvent(agentTask);
                enumerator.ProcessingTask = Task.Run(async () =>
                {
                    using var createActivity = ActivitySource.StartActivity("OnTaskCreated", ActivityKind.Server);
                    await OnTaskCreated(agentTask, cancellationToken).ConfigureAwait(false);
                }, cancellationToken);
            }
        }
        else
        {
            // If the task is found, update its status and history
            agentTask.History ??= [];
            agentTask.History.Add(messageSendParams.Message);

            await _taskStore.SetTaskAsync(agentTask, cancellationToken).ConfigureAwait(false);
            enumerator = new TaskUpdateEventEnumerator();
            _taskUpdateEventEnumerators[agentTask.Id] = enumerator;
            enumerator.NotifyEvent(agentTask);
            enumerator.ProcessingTask = Task.Run(async () =>
            {
                using var createActivity = ActivitySource.StartActivity("OnTaskUpdated", ActivityKind.Server);
                await OnTaskUpdated(agentTask, cancellationToken).ConfigureAwait(false);
            }, cancellationToken);
        }

        await foreach (var i in enumerator.WithCancellation(cancellationToken))
        {
            yield return i;
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<A2AEvent> SubscribeToTaskAsync(TaskIdParams taskIdParams, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (taskIdParams is null)
        {
            throw new A2AException(nameof(taskIdParams), A2AErrorCode.InvalidParams);
        }

        using var activity = ActivitySource.StartActivity("SubscribeToTask", ActivityKind.Server);
        activity?.SetTag("task.id", taskIdParams.Id);

        return _taskUpdateEventEnumerators.TryGetValue(taskIdParams.Id, out var enumerator) ?
            (IAsyncEnumerable<A2AEvent>)enumerator :
            throw new A2AException("Task not found or invalid TaskIdParams.", A2AErrorCode.TaskNotFound);
    }

    /// <inheritdoc />
    public async Task<TaskPushNotificationConfig?> SetPushNotificationAsync(TaskPushNotificationConfig pushNotificationConfig, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (pushNotificationConfig is null)
        {
            throw new A2AException(nameof(pushNotificationConfig), A2AErrorCode.InvalidParams);
        }

        await _taskStore.SetPushNotificationConfigAsync(pushNotificationConfig, cancellationToken).ConfigureAwait(false);
        return pushNotificationConfig;
    }

    /// <inheritdoc />
    public async Task<TaskPushNotificationConfig?> GetPushNotificationAsync(GetTaskPushNotificationConfigParams? notificationConfigParams, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (notificationConfigParams is null)
        {
            throw new A2AException("GetTaskPushNotificationConfigParams cannot be null.", A2AErrorCode.InvalidParams);
        }

        using var activity = ActivitySource.StartActivity("GetPushNotification", ActivityKind.Server);
        activity?.SetTag("task.id", notificationConfigParams.Id);
        activity?.SetTag("push.config.id", notificationConfigParams.PushNotificationConfigId);

        var task = await _taskStore.GetTaskAsync(notificationConfigParams.Id, cancellationToken).ConfigureAwait(false);
        if (task == null)
        {
            activity?.SetTag("task.found", false);
            throw new ArgumentException($"Task with {notificationConfigParams.Id} not found.");
        }

        TaskPushNotificationConfig? pushNotificationConfig = null;

        if (!string.IsNullOrEmpty(notificationConfigParams.PushNotificationConfigId))
        {
            pushNotificationConfig = await _taskStore.GetPushNotificationAsync(notificationConfigParams.Id, notificationConfigParams.PushNotificationConfigId!, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var pushNotificationConfigs = await _taskStore.GetPushNotificationsAsync(notificationConfigParams.Id, cancellationToken).ConfigureAwait(false);

            pushNotificationConfig = pushNotificationConfigs.FirstOrDefault();
        }

        activity?.SetTag("config.found", pushNotificationConfig != null);
        return pushNotificationConfig;
    }

    /// <inheritdoc />
    public async Task UpdateStatusAsync(string taskId, TaskState status, Message? message = null, bool final = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(taskId))
        {
            throw new A2AException(nameof(taskId), A2AErrorCode.InvalidParams);
        }

        using var activity = ActivitySource.StartActivity("UpdateStatus", ActivityKind.Server);
        activity?.SetTag("task.id", taskId);
        activity?.SetTag("task.status", status.ToString());
        activity?.SetTag("task.finalStatus", final);

        try
        {
            var agentStatus = await _taskStore.UpdateStatusAsync(taskId, status, message, cancellationToken).ConfigureAwait(false);
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
    public async Task ReturnArtifactAsync(string taskId, Artifact artifact, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(taskId))
        {
            throw new A2AException(nameof(taskId), A2AErrorCode.InvalidParams);
        }
        else if (artifact is null)
        {
            throw new A2AException(nameof(artifact), A2AErrorCode.InvalidParams);
        }

        using var activity = ActivitySource.StartActivity("ReturnArtifact", ActivityKind.Server);
        activity?.SetTag("task.id", taskId);

        try
        {
            var task = await _taskStore.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
            if (task != null)
            {
                activity?.SetTag("task.found", true);

                task.Artifacts ??= [];
                task.Artifacts.Add(artifact);
                await _taskStore.SetTaskAsync(task, cancellationToken).ConfigureAwait(false);

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
                throw new A2AException("Task not found.", A2AErrorCode.TaskNotFound);
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

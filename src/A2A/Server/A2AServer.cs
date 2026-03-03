using A2A.Extensions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace A2A;

/// <summary>
/// A2A server: orchestrates request lifecycle, context resolution, task persistence,
/// history management, terminal state guards, cancel support, and observability.
/// Implements <see cref="IA2ARequestHandler"/> for the easy path where agent authors
/// provide an <see cref="IAgentHandler"/> and the SDK handles everything else.
/// </summary>
public class A2AServer : IA2ARequestHandler
{
    private readonly IAgentHandler _handler;
    private readonly ITaskStore _taskStore;
    private readonly ChannelEventNotifier _notifier;
    private readonly ILogger<A2AServer> _logger;
    private readonly A2AServerOptions _options;

    /// <summary>Initializes a new instance of the <see cref="A2AServer"/> class.</summary>
    /// <param name="handler">The agent handler that provides execution logic.</param>
    /// <param name="taskStore">The task store used for persistence.</param>
    /// <param name="notifier">The event notifier for live event streaming and per-task locking.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="options">Optional configuration options.</param>
    public A2AServer(IAgentHandler handler, ITaskStore taskStore,
        ChannelEventNotifier notifier, ILogger<A2AServer> logger,
        A2AServerOptions? options = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new A2AServerOptions();
    }

    /// <inheritdoc />
    public virtual async Task<SendMessageResponse> SendMessageAsync(
        SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = A2ADiagnostics.Source.StartActivity("A2AServer.SendMessage", ActivityKind.Internal);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            A2ADiagnostics.RequestCount.Add(1);

            var context = await ResolveContextAsync(request, isStreaming: false, cancellationToken).ConfigureAwait(false);
            TagActivity(activity, context);
            GuardTerminalState(context);

            if (context.IsContinuation && _options.AutoAppendHistory)
            {
                await PersistEventAsync(
                    new StreamResponse { Message = request.Message },
                    context, cancellationToken).ConfigureAwait(false);
            }

            var eventQueue = new AgentEventQueue();
            var agentTask = Task.Run(async () =>
            {
                try
                {
                    await _handler.ExecuteAsync(context, eventQueue, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    eventQueue.Complete();
                }
            }, cancellationToken);

            var result = await MaterializeResponseAsync(eventQueue, context, cancellationToken).ConfigureAwait(false);
            await agentTask.ConfigureAwait(false); // surface handler exceptions
            return result;
        }
        catch (Exception ex)
        {
            A2ADiagnostics.ErrorCount.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            RecordException(activity, ex);
            throw;
        }
        finally
        {
            A2ADiagnostics.RequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(
        SendMessageRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = A2ADiagnostics.Source.StartActivity("A2AServer.SendStreamingMessage", ActivityKind.Internal);
        A2ADiagnostics.RequestCount.Add(1);

        AgentContext? context = null;
        AgentEventQueue? eventQueue = null;
        Task? agentTask = null;
        int eventCount = 0;

        try
        {
            context = await ResolveContextAsync(request, isStreaming: true, cancellationToken).ConfigureAwait(false);
            TagActivity(activity, context);
            GuardTerminalState(context);

            if (context.IsContinuation && _options.AutoAppendHistory)
            {
                await PersistEventAsync(
                    new StreamResponse { Message = request.Message },
                    context, cancellationToken).ConfigureAwait(false);
            }

            eventQueue = new AgentEventQueue();
            agentTask = Task.Run(async () =>
            {
                try
                {
                    await _handler.ExecuteAsync(context, eventQueue, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    eventQueue.Complete();
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            A2ADiagnostics.ErrorCount.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            RecordException(activity, ex);
            throw;
        }

        await foreach (var response in eventQueue.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (_options.AutoPersistEvents)
            {
                await PersistEventAsync(response, context!, cancellationToken).ConfigureAwait(false);
            }

            eventCount++;
            yield return response;
        }

        A2ADiagnostics.StreamEventCount.Record(eventCount);

        // Surface any agent exceptions
        await agentTask!.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<AgentTask> GetTaskAsync(
        GetTaskRequest request, CancellationToken cancellationToken = default)
    {
        var task = await _taskStore.GetTaskAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new A2AException($"Task '{request.Id}' not found.", A2AErrorCode.TaskNotFound);

        return task.WithHistoryTrimmedTo(request.HistoryLength);
    }

    /// <inheritdoc />
    public virtual async Task<ListTasksResponse> ListTasksAsync(
        ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        return await _taskStore.ListTasksAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<AgentTask> CancelTaskAsync(
        CancelTaskRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = A2ADiagnostics.Source.StartActivity("A2AServer.CancelTask", ActivityKind.Internal);
        activity?.SetTag("a2a.task.id", request.Id);

        var task = await _taskStore.GetTaskAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new A2AException($"Task '{request.Id}' not found.", A2AErrorCode.TaskNotFound);

        if (task.Status.State.IsTerminal())
        {
            throw new A2AException("Task is already in a terminal state.", A2AErrorCode.TaskNotCancelable);
        }

        var context = new AgentContext
        {
            Message = task.History?.LastOrDefault() ?? new Message { Role = Role.User, MessageId = string.Empty, Parts = [] },
            Task = task,
            TaskId = task.Id,
            ContextId = task.ContextId,
            IsStreaming = false,
        };

        var eventQueue = new AgentEventQueue();
        try
        {
            await _handler.CancelAsync(context, eventQueue, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            eventQueue.Complete();
        }

        await foreach (var response in eventQueue.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await PersistEventAsync(response, context, cancellationToken).ConfigureAwait(false);
        }

        return await _taskStore.GetTaskAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new A2AException($"Task '{request.Id}' not found.", A2AErrorCode.TaskNotFound);
    }

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(
        SubscribeToTaskRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = A2ADiagnostics.Source.StartActivity("A2AServer.SubscribeToTask", ActivityKind.Internal);
        activity?.SetTag("a2a.task.id", request.Id);

        AgentTask currentTask;
        Channel<StreamResponse> channel;

        // Atomic: read task state + register subscriber channel under per-task lock.
        // Concurrent PersistEventAsync calls block until the channel is registered,
        // guaranteeing no events are lost between snapshot and live stream.
        using (await _notifier.AcquireTaskLockAsync(request.Id, cancellationToken).ConfigureAwait(false))
        {
            currentTask = await _taskStore.GetTaskAsync(request.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new A2AException($"Task '{request.Id}' not found.", A2AErrorCode.TaskNotFound);

            if (currentTask.Status.State.IsTerminal())
            {
                throw new A2AException(
                    "Task is in a terminal state and cannot be subscribed to.",
                    A2AErrorCode.UnsupportedOperation);
            }

            channel = _notifier.CreateChannel(request.Id);
        }

        // First event MUST be current Task object (spec §3.1.6)
        yield return new StreamResponse { Task = currentTask };

        // Live events via channel (no catch-up needed — lock guarantees no gap)
        try
        {
            await foreach (var streamEvent in channel.Reader.ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return streamEvent;
            }
        }
        finally
        {
            _notifier.RemoveChannel(request.Id, channel);
        }
    }

    /// <inheritdoc />
    public virtual Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(
        CreateTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        throw new A2AException("Push notifications not supported.", A2AErrorCode.PushNotificationNotSupported);
    }

    /// <inheritdoc />
    public virtual Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(
        GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        throw new A2AException("Push notifications not supported.", A2AErrorCode.PushNotificationNotSupported);
    }

    /// <inheritdoc />
    public virtual Task<ListTaskPushNotificationConfigResponse> ListTaskPushNotificationConfigAsync(
        ListTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        throw new A2AException("Push notifications not supported.", A2AErrorCode.PushNotificationNotSupported);
    }

    /// <inheritdoc />
    public virtual Task DeleteTaskPushNotificationConfigAsync(
        DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        throw new A2AException("Push notifications not supported.", A2AErrorCode.PushNotificationNotSupported);
    }

    /// <inheritdoc />
    public virtual Task<AgentCard> GetExtendedAgentCardAsync(
        GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default)
    {
        throw new A2AException("Extended agent card not configured.", A2AErrorCode.ExtendedAgentCardNotConfigured);
    }

    // ─── Private Helpers ───

    private async Task<AgentContext> ResolveContextAsync(
        SendMessageRequest request, bool isStreaming, CancellationToken cancellationToken)
    {
        AgentTask? existingTask = null;
        var taskId = request.Message.TaskId;
        var contextId = request.Message.ContextId;

        if (!string.IsNullOrEmpty(taskId))
        {
            existingTask = await _taskStore.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false)
                ?? throw new A2AException($"Task '{taskId}' not found.", A2AErrorCode.TaskNotFound);
            contextId ??= existingTask.ContextId;
        }

        return new AgentContext
        {
            Message = request.Message,
            Task = existingTask,
            TaskId = taskId ?? Guid.NewGuid().ToString("N"),
            ContextId = contextId ?? Guid.NewGuid().ToString("N"),
            IsStreaming = isStreaming,
            Metadata = request.Metadata,
        };
    }

    private static void GuardTerminalState(AgentContext context)
    {
        if (context.Task is not null && context.Task.Status.State.IsTerminal())
        {
            throw new A2AException(
                "Task is in a terminal state and cannot accept messages.",
                A2AErrorCode.UnsupportedOperation);
        }
    }

    private async Task PersistEventAsync(
        StreamResponse response, AgentContext context, CancellationToken cancellationToken)
    {
        if (response.Task is not null)
        {
            A2ADiagnostics.TaskCreatedCount.Add(1);
        }

        using (await _notifier.AcquireTaskLockAsync(context.TaskId, cancellationToken).ConfigureAwait(false))
        {
            var currentTask = await _taskStore.GetTaskAsync(context.TaskId, cancellationToken)
                .ConfigureAwait(false);

            var updatedTask = TaskProjection.Apply(currentTask, response);

            // Message-only responses with no existing task have nothing to persist.
            if (updatedTask is null)
            {
                _notifier.Notify(context.TaskId, response);
                return;
            }

            await _taskStore.SaveTaskAsync(context.TaskId, updatedTask, cancellationToken)
                .ConfigureAwait(false);

            _notifier.Notify(context.TaskId, response);
        }
    }

    private async Task<SendMessageResponse> MaterializeResponseAsync(
        AgentEventQueue eventQueue, AgentContext context, CancellationToken cancellationToken)
    {
        SendMessageResponse? result = null;

        await foreach (var response in eventQueue.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (_options.AutoPersistEvents)
            {
                await PersistEventAsync(response, context, cancellationToken).ConfigureAwait(false);
            }

            // Capture the first Task or Message as the synchronous response
            if (result is null)
            {
                if (response.Task is not null)
                {
                    result = new SendMessageResponse { Task = response.Task };
                }
                else if (response.Message is not null)
                {
                    result = new SendMessageResponse { Message = response.Message };
                }
            }
        }

        // Re-fetch the projected task to ensure the response reflects
        // all persisted events, not a stale snapshot.
        if (result?.Task is not null)
        {
            result.Task = await _taskStore.GetTaskAsync(context.TaskId, cancellationToken).ConfigureAwait(false)
                ?? throw new A2AException($"Task '{context.TaskId}' not found after processing.", A2AErrorCode.TaskNotFound);
        }

        return result ?? throw new A2AException(
            "Agent handler did not produce any response events.",
            A2AErrorCode.InvalidAgentResponse);
    }

    private static void TagActivity(Activity? activity, AgentContext context)
    {
        activity?.SetTag("a2a.task.id", context.TaskId);
        activity?.SetTag("a2a.context.id", context.ContextId);
        activity?.SetTag("a2a.is_continuation", context.IsContinuation);
        activity?.SetTag("a2a.is_streaming", context.IsStreaming);
    }

    private static void RecordException(Activity? activity, Exception ex)
    {
        if (activity is null)
        {
            return;
        }

        var tags = new ActivityTagsCollection
        {
            { "exception.type", ex.GetType().FullName },
            { "exception.message", ex.Message },
        };

        activity.AddEvent(new ActivityEvent("exception", tags: tags));
    }
}

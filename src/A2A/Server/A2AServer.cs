using A2A.Extensions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
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
public class A2AServer : IA2ARequestHandler, IAsyncDisposable
{
    private readonly IAgentHandler _handler;
    private readonly ITaskStore _taskStore;
    private readonly ChannelEventNotifier _notifier;
    private readonly ILogger<A2AServer> _logger;
    private readonly A2AServerOptions _options;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _backgroundCancellations = new();
    private readonly ConcurrentDictionary<string, Task> _backgroundTasks = new();

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

    /// <summary>Cancels and awaits all background return-immediately drain tasks.</summary>
    public async ValueTask DisposeAsync()
    {
        // Cancel all background work. Each drain's finally block owns its own
        // dictionary removal and CTS disposal, so no cleanup needed here.
        foreach (var cts in _backgroundCancellations.Values.ToArray())
        {
            try { await cts.CancelAsync().ConfigureAwait(false); }
            catch (ObjectDisposedException) { }
        }

        await Task.WhenAll(_backgroundTasks.Values.ToArray()).ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public virtual async Task<SendMessageResponse> SendMessageAsync(
        SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = A2ADiagnostics.Source.StartActivity("A2AServer.SendMessage", ActivityKind.Internal);
        var stopwatch = Stopwatch.StartNew();
        RequestContext? context = null;
        CancellationTokenSource? backgroundCts = null;

        try
        {
            A2ADiagnostics.RequestCount.Add(1);

            context = await ResolveContextAsync(request, streamingResponse: false, cancellationToken).ConfigureAwait(false);
            TagActivity(activity, context);
            GuardTerminalState(context);

            if (context.IsContinuation && _options.AutoAppendHistory)
            {
                await ApplyEventAsync(
                    new StreamResponse { Message = request.Message },
                    context, cancellationToken).ConfigureAwait(false);
            }

            bool returnImmediately = request.Configuration?.ReturnImmediately == true;

            var eventQueue = new AgentEventQueue();
            // For return-immediately, use a long-lived CTS that outlives the HTTP request
            // but can be cancelled by a subsequent tasks/cancel call.
            // For continuations, reuse the existing CTS so one cancel kills all handlers.
            CancellationToken executionCancellationToken;
            if (returnImmediately)
            {
                if (_backgroundCancellations.TryGetValue(context.TaskId, out var existingCts))
                {
                    try
                    {
                        executionCancellationToken = existingCts.Token;
                    }
                    catch (ObjectDisposedException)
                    {
                        // CTS was disposed by a concurrent drain completion — create a fresh one
                        backgroundCts = new CancellationTokenSource();
                        _backgroundCancellations[context.TaskId] = backgroundCts;
                        executionCancellationToken = backgroundCts.Token;
                    }
                }
                else
                {
                    backgroundCts = new CancellationTokenSource();
                    _backgroundCancellations[context.TaskId] = backgroundCts;
                    executionCancellationToken = backgroundCts.Token;
                }
            }
            else
            {
                executionCancellationToken = cancellationToken;
            }

            var agentTask = Task.Run(async () =>
            {
                try
                {
                    await _handler.ExecuteAsync(context, eventQueue, executionCancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    eventQueue.Complete();
                }
            }, executionCancellationToken);

            if (returnImmediately)
            {
                return await MaterializeReturnImmediatelyResponseAsync(
                    eventQueue, agentTask, context,
                    backgroundCts, executionCancellationToken, cancellationToken).ConfigureAwait(false);
            }

            var result = await MaterializeResponseAsync(eventQueue, context, cancellationToken).ConfigureAwait(false);
            await agentTask.ConfigureAwait(false); // surface handler exceptions

            return result;
        }
        catch (Exception ex)
        {
            // Clean up orphaned background CTS if we registered one but never
            // reached the drain task that owns its removal and disposal.
            if (backgroundCts is not null && context is not null &&
                _backgroundCancellations.TryRemove(context.TaskId, out _))
            {
                backgroundCts.Dispose();
            }

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

        RequestContext? context = null;
        AgentEventQueue? eventQueue = null;
        Task? agentTask = null;
        int eventCount = 0;

        try
        {
            context = await ResolveContextAsync(request, streamingResponse: true, cancellationToken).ConfigureAwait(false);
            TagActivity(activity, context);
            GuardTerminalState(context);

            if (context.IsContinuation && _options.AutoAppendHistory)
            {
                await ApplyEventAsync(
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

        try
        {
            await foreach (var response in eventQueue.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await ApplyEventAsync(response, context!, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    A2ADiagnostics.ErrorCount.Add(1);
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    RecordException(activity, ex);
                    yield break;
                }

                eventCount++;
                yield return response;
            }

            // Surface any agent exceptions
            await agentTask!.ConfigureAwait(false);
        }
        finally
        {
            A2ADiagnostics.StreamEventCount.Record(eventCount);
        }
    }

    /// <inheritdoc />
    public virtual async Task<AgentTask> GetTaskAsync(
        GetTaskRequest request, CancellationToken cancellationToken = default)
    {
        if (request.HistoryLength is { } hl && hl < 0)
        {
            throw new A2AException(
                $"Invalid historyLength: {hl}. Must be non-negative.",
                A2AErrorCode.InvalidParams);
        }

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

        // Signal any background return-immediately work to stop.
        // Don't dispose the CTS here — the drain's finally block owns disposal.
        if (_backgroundCancellations.TryRemove(request.Id, out var backgroundCts))
        {
            await backgroundCts.CancelAsync().ConfigureAwait(false);
        }

        var context = new RequestContext
        {
            Message = task.History?.LastOrDefault() ?? new Message { Role = Role.User, MessageId = string.Empty, Parts = [] },
            Task = task,
            TaskId = task.Id,
            ContextId = task.ContextId,
            StreamingResponse = false,
            Metadata = request.Metadata,
        };

        var eventQueue = new AgentEventQueue();
        var agentTask = Task.Run(async () =>
        {
            try
            {
                await _handler.CancelAsync(context, eventQueue, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                eventQueue.Complete();
            }
        }, cancellationToken);

        await foreach (var response in eventQueue.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await ApplyEventAsync(response, context, cancellationToken).ConfigureAwait(false);
        }

        await agentTask.ConfigureAwait(false);

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
        // Concurrent ApplyEventAsync calls block until the channel is registered,
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

    private async Task<RequestContext> ResolveContextAsync(
        SendMessageRequest request, bool streamingResponse, CancellationToken cancellationToken)
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

        return new RequestContext
        {
            Message = request.Message,
            Task = existingTask,
            TaskId = taskId ?? Guid.NewGuid().ToString("N"),
            ContextId = contextId ?? Guid.NewGuid().ToString("N"),
            ClientProvidedContextId = contextId is not null,
            StreamingResponse = streamingResponse,
            Configuration = request.Configuration,
            Metadata = request.Metadata,
        };
    }

    private static void GuardTerminalState(RequestContext context)
    {
        if (context.Task is not null && context.Task.Status.State.IsTerminal())
        {
            throw new A2AException(
                "Task is in a terminal state and cannot accept messages.",
                A2AErrorCode.UnsupportedOperation);
        }
    }

    private async Task ApplyEventAsync(
        StreamResponse response, RequestContext context, CancellationToken cancellationToken)
    {
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

            if (currentTask is null)
            {
                A2ADiagnostics.TaskCreatedCount.Add(1);
            }

            await _taskStore.SaveTaskAsync(context.TaskId, updatedTask, cancellationToken)
                .ConfigureAwait(false);

            _notifier.Notify(context.TaskId, response);
        }
    }

    /// <summary>
    /// Reads events until the first Task event, returns it immediately, and drains
    /// remaining events in the background. Message responses are returned normally
    /// (return-immediately has no effect on Message responses per spec).
    /// </summary>
    /// <param name="eventQueue">The event queue produced by the agent handler.</param>
    /// <param name="agentTask">The background task running the agent handler.</param>
    /// <param name="context">The request context for the current operation.</param>
    /// <param name="ownedBackgroundCts">The CTS to dispose on completion, or null if reusing an existing CTS owned by a prior drain.</param>
    /// <param name="backgroundCancellationToken">Token for cancelling the background drain (from shared or new CTS).</param>
    /// <param name="cancellationToken">Token for cancelling the initial read.</param>
    private async Task<SendMessageResponse> MaterializeReturnImmediatelyResponseAsync(
        AgentEventQueue eventQueue, Task agentTask, RequestContext context,
        CancellationTokenSource? ownedBackgroundCts,
        CancellationToken backgroundCancellationToken, CancellationToken cancellationToken)
    {
        SendMessageResponse? result = null;

        await foreach (var response in eventQueue.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await ApplyEventAsync(response, context, CancellationToken.None).ConfigureAwait(false);

            if (result is null)
            {
                if (response.Task is not null)
                {
                    result = new SendMessageResponse { Task = response.Task };
                    break; // Return immediately with first Task event
                }

                if (response.Message is not null)
                {
                    // Message response — returnImmediately has no effect, drain normally
                    result = new SendMessageResponse { Message = response.Message };
                }
            }
        }

        if (result?.Task is not null)
        {
            // Drain remaining events in the background so they are applied to the task store.
            var drainTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var response in eventQueue.WithCancellation(backgroundCancellationToken).ConfigureAwait(false))
                    {
                        await ApplyEventAsync(response, context, CancellationToken.None).ConfigureAwait(false);
                    }

#pragma warning disable VSTHRD003 // Intentional: agentTask runs the agent handler in the background
                    await agentTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
                }
                catch (OperationCanceledException)
                {
                    // Expected when tasks/cancel triggers the backgroundCts
                }
                catch (Exception ex)
                {
                    _logger.BackgroundEventProcessingFailed(ex, context.TaskId);
                }
                finally
                {
                    _backgroundTasks.TryRemove(context.TaskId, out _);

                    // Only clean up the CTS if this drain owns it (i.e., it was created
                    // for this request, not reused from an earlier return-immediately call).
                    if (ownedBackgroundCts is not null)
                    {
                        _backgroundCancellations.TryRemove(context.TaskId, out _);
                        ownedBackgroundCts.Dispose();
                    }
                }
            }, CancellationToken.None);

            _backgroundTasks[context.TaskId] = drainTask;

            // Re-fetch from store to return the current persisted state
            result.Task = await _taskStore.GetTaskAsync(context.TaskId, CancellationToken.None).ConfigureAwait(false)
                ?? throw new A2AException($"Task '{context.TaskId}' not found after processing.", A2AErrorCode.TaskNotFound);

            return result;
        }

        // Message response or queue fully drained — wait for handler to surface exceptions
#pragma warning disable VSTHRD003 // Intentional: agentTask was started within SendMessageAsync
        await agentTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003

        return result ?? throw new A2AException(
            "Agent handler did not produce any response events.",
            A2AErrorCode.InvalidAgentResponse);
    }

    private async Task<SendMessageResponse> MaterializeResponseAsync(
        AgentEventQueue eventQueue, RequestContext context, CancellationToken cancellationToken)
    {
        SendMessageResponse? result = null;

        await foreach (var response in eventQueue.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await ApplyEventAsync(response, context, cancellationToken).ConfigureAwait(false);

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

    private static void TagActivity(Activity? activity, RequestContext context)
    {
        activity?.SetTag("a2a.task.id", context.TaskId);
        activity?.SetTag("a2a.context.id", context.ContextId);
        activity?.SetTag("a2a.is_continuation", context.IsContinuation);
        activity?.SetTag("a2a.streaming_response", context.StreamingResponse);
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

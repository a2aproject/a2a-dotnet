using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

public partial class A2AServer
{
    /// <summary>Produces events for an existing non-terminal task without an inbound A2A request.</summary>
    /// <param name="taskId">The existing, host-authorized task to continue.</param>
    /// <param name="producer">Produces task-scoped events using the supplied queue.</param>
    /// <param name="cancellationToken">Cancels production and pending publication.</param>
    /// <returns>Final persisted state and counts of committed events and accepted event/registration pairs.</returns>
    /// <exception cref="TaskContinuationException">A prerequisite or execution stage failed; committed progress is included.</exception>
    /// <remarks>
    /// Requires an active task-scoped push registration. Same-task executions sharing a notifier
    /// are serialized for their complete producer/publication lifetime. This is process-local,
    /// not distributed coordination. Acceptance does not imply webhook receipt. The host must
    /// authorize external triggers before calling this method. Producers must honor cancellation
    /// and must not await another execution for the same task.
    /// </remarks>
    public async Task<TaskContinuationResult> ContinueTaskAsync(
        string taskId,
        Func<TaskContinuationContext, CancellationToken, Task> producer,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentNullException.ThrowIfNull(producer);
        var progress = new TaskContinuationProgress();
        var execution = StartExecution(taskId,
            token => ResolveContinuationContextAsync(taskId, progress, token),
            detachedLifetime: false, cancelHandler: false, cancellationToken, producer, progress);
        return await execution.ContinueAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<RequestContext> ResolveContinuationContextAsync(
        string taskId, TaskContinuationProgress progress, CancellationToken cancellationToken)
    {
        progress.FailureKind = TaskContinuationFailureKind.TaskStore;
        var task = await _taskStore.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            throw progress.Error(TaskContinuationFailureKind.TaskNotFound, $"Task '{taskId}' not found.");
        }
        progress.Task = SnapshotTask(task);
        if (task.Status.State.IsTerminal())
        {
            throw progress.Error(TaskContinuationFailureKind.NonContinuableTask, $"Task '{taskId}' is terminal and cannot be continued.");
        }
        progress.FailureKind = TaskContinuationFailureKind.PushNotificationStore;
        var configurations = _pushNotifications is null
            ? []
            : await _pushNotifications.ReadConfigurationsAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (!configurations.Any(config => !config.RemovalToken.IsCancellationRequested))
        {
            throw progress.Error(TaskContinuationFailureKind.MissingPushRegistration,
                $"Task '{taskId}' has no active push-notification registration. Register a delivery route before continuing the task.");
        }

        return new RequestContext(taskId, SnapshotTask(task), progress);
    }

    private async Task<RequestContext> RefreshExecutionContextAsync(
        RequestContext context, bool cancelHandler, CancellationToken cancellationToken)
    {
        if (context.Task is null)
        {
            return context;
        }
        var task = await _taskStore.GetTaskAsync(context.TaskId, cancellationToken).ConfigureAwait(false)
            ?? throw new A2AException($"Task '{context.TaskId}' not found.", A2AErrorCode.TaskNotFound);
        if (cancelHandler && task.Status.State.IsTerminal())
        {
            throw new A2AException("Task is already in a terminal state.", A2AErrorCode.TaskNotCancelable);
        }
        var refreshed = new RequestContext
        {
            Message = context.Message,
            Task = task,
            TaskId = context.TaskId,
            ContextId = task.ContextId,
            ClientProvidedContextId = context.ClientProvidedContextId,
            StreamingResponse = context.StreamingResponse,
            Configuration = context.Configuration,
            Metadata = context.Metadata,
            ExtensionActivation = context.ExtensionActivation,
            PushNotifications = context.PushNotifications,
        };
        GuardTerminalState(refreshed);
        return refreshed;
    }

    private static AgentTask SnapshotTask(AgentTask task)
    {
        var typeInfo = (JsonTypeInfo<AgentTask>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(AgentTask));
        return JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(task, typeInfo), typeInfo)!;
    }

    private static void ValidateContinuationEvent(StreamResponse response, RequestContext context)
    {
        var taskId = response.Task?.Id ?? response.Message?.TaskId ??
            response.StatusUpdate?.TaskId ?? response.ArtifactUpdate?.TaskId;
        var contextId = response.Task?.ContextId ?? response.Message?.ContextId ??
            response.StatusUpdate?.ContextId ?? response.ArtifactUpdate?.ContextId;
        int payloadCount = (response.Task is null ? 0 : 1) + (response.Message is null ? 0 : 1) +
            (response.StatusUpdate is null ? 0 : 1) + (response.ArtifactUpdate is null ? 0 : 1);
        if (payloadCount != 1 ||
            !string.Equals(taskId, context.TaskId, StringComparison.Ordinal) ||
            !string.Equals(contextId, context.ContextId, StringComparison.Ordinal))
        {
            throw context.ContinuationProgress!.Error(TaskContinuationFailureKind.InvalidEvent,
                "Continuation events must contain a payload with the continuation task ID and context ID.");
        }
    }
}

internal sealed class TaskContinuationProgress
{
    internal AgentTask? Task { get; set; }
    internal int PersistedEventCount { get; set; }
    internal int AcceptedPushNotificationCount { get; set; }
    internal TaskContinuationFailureKind FailureKind { get; set; } = TaskContinuationFailureKind.TaskStore;
    internal TaskContinuationResult? Result => Task is { } task
        ? new(task, PersistedEventCount, AcceptedPushNotificationCount)
        : null;

    internal TaskContinuationException Error(TaskContinuationFailureKind kind, string message, Exception? innerException = null) =>
        new(kind, message, Result, innerException);
}

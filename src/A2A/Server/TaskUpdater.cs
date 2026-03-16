namespace A2A;

/// <summary>
/// Convenience wrapper around <see cref="AgentEventQueue"/> for common
/// task lifecycle operations. Handles timestamps and event construction.
/// Usable by both <see cref="IAgentHandler"/> implementations (easy path)
/// and direct consumers (difficult path).
/// </summary>
/// <param name="eventQueue">The event queue to write lifecycle events to.</param>
/// <param name="taskId">The task ID to operate on.</param>
/// <param name="contextId">The context ID to operate on.</param>
public sealed class TaskUpdater(AgentEventQueue eventQueue, string taskId, string contextId)
{
    /// <summary>Gets the task ID this updater operates on.</summary>
    public string TaskId => taskId;

    /// <summary>Gets the context ID this updater operates on.</summary>
    public string ContextId => contextId;

    /// <summary>Emit the initial task with Submitted status.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask SubmitAsync(CancellationToken cancellationToken = default)
        => eventQueue.EnqueueTaskAsync(new AgentTask
        {
            Id = taskId,
            ContextId = contextId,
            Status = new TaskStatus
            {
                State = TaskState.Submitted,
                Timestamp = DateTimeOffset.UtcNow,
            },
        }, cancellationToken);

    /// <summary>Transition task to Working state with an optional status message.</summary>
    /// <param name="message">Optional message describing the current work.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask StartWorkAsync(Message? message = null, CancellationToken cancellationToken = default)
        => eventQueue.EnqueueStatusUpdateAsync(new TaskStatusUpdateEvent
        {
            TaskId = taskId,
            ContextId = contextId,
            Status = new TaskStatus
            {
                State = TaskState.Working,
                Timestamp = DateTimeOffset.UtcNow,
                Message = message,
            },
        }, cancellationToken);

    /// <summary>Add an artifact to the task.</summary>
    /// <param name="parts">The content parts of the artifact.</param>
    /// <param name="artifactId">Optional artifact ID; auto-generated if null.</param>
    /// <param name="name">Optional artifact name.</param>
    /// <param name="description">Optional artifact description.</param>
    /// <param name="lastChunk">Whether this is the final chunk for this artifact.</param>
    /// <param name="append">Whether to append to an existing artifact with the same ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask AddArtifactAsync(
        IReadOnlyList<Part> parts,
        string? artifactId = null,
        string? name = null,
        string? description = null,
        bool lastChunk = true,
        bool append = false,
        CancellationToken cancellationToken = default)
        => eventQueue.EnqueueArtifactUpdateAsync(new TaskArtifactUpdateEvent
        {
            TaskId = taskId,
            ContextId = contextId,
            Artifact = new Artifact
            {
                ArtifactId = artifactId ?? Guid.NewGuid().ToString("N"),
                Name = name,
                Description = description,
                Parts = [.. parts],
            },
            Append = append,
            LastChunk = lastChunk,
        }, cancellationToken);

    /// <summary>Complete the task with an optional final message.</summary>
    /// <param name="message">Optional completion message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask CompleteAsync(Message? message = null, CancellationToken cancellationToken = default)
    {
        await eventQueue.EnqueueStatusUpdateAsync(new TaskStatusUpdateEvent
        {
            TaskId = taskId,
            ContextId = contextId,
            Status = new TaskStatus
            {
                State = TaskState.Completed,
                Timestamp = DateTimeOffset.UtcNow,
                Message = message,
            },
        }, cancellationToken);
        eventQueue.Complete();
    }

    /// <summary>Fail the task with an optional error message.</summary>
    /// <param name="message">Optional error message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask FailAsync(Message? message = null, CancellationToken cancellationToken = default)
    {
        await eventQueue.EnqueueStatusUpdateAsync(new TaskStatusUpdateEvent
        {
            TaskId = taskId,
            ContextId = contextId,
            Status = new TaskStatus
            {
                State = TaskState.Failed,
                Timestamp = DateTimeOffset.UtcNow,
                Message = message,
            },
        }, cancellationToken);
        eventQueue.Complete();
    }

    /// <summary>Cancel the task.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask CancelAsync(CancellationToken cancellationToken = default)
    {
        await eventQueue.EnqueueStatusUpdateAsync(new TaskStatusUpdateEvent
        {
            TaskId = taskId,
            ContextId = contextId,
            Status = new TaskStatus
            {
                State = TaskState.Canceled,
                Timestamp = DateTimeOffset.UtcNow,
            },
        }, cancellationToken);
        eventQueue.Complete();
    }

    /// <summary>Reject the task with an optional reason message.</summary>
    /// <param name="message">Optional rejection reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask RejectAsync(Message? message = null, CancellationToken cancellationToken = default)
    {
        await eventQueue.EnqueueStatusUpdateAsync(new TaskStatusUpdateEvent
        {
            TaskId = taskId,
            ContextId = contextId,
            Status = new TaskStatus
            {
                State = TaskState.Rejected,
                Timestamp = DateTimeOffset.UtcNow,
                Message = message,
            },
        }, cancellationToken);
        eventQueue.Complete();
    }

    /// <summary>Request additional input from the client.</summary>
    /// <param name="message">Message describing what input is needed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask RequireInputAsync(Message message, CancellationToken cancellationToken = default)
    {
        await eventQueue.EnqueueStatusUpdateAsync(new TaskStatusUpdateEvent
        {
            TaskId = taskId,
            ContextId = contextId,
            Status = new TaskStatus
            {
                State = TaskState.InputRequired,
                Timestamp = DateTimeOffset.UtcNow,
                Message = message,
            },
        }, cancellationToken);
        eventQueue.Complete();
    }

    /// <summary>Request authentication from the client.</summary>
    /// <param name="message">Optional message describing the auth requirement.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask RequireAuthAsync(Message? message = null, CancellationToken cancellationToken = default)
    {
        await eventQueue.EnqueueStatusUpdateAsync(new TaskStatusUpdateEvent
        {
            TaskId = taskId,
            ContextId = contextId,
            Status = new TaskStatus
            {
                State = TaskState.AuthRequired,
                Timestamp = DateTimeOffset.UtcNow,
                Message = message,
            },
        }, cancellationToken);
        eventQueue.Complete();
    }
}

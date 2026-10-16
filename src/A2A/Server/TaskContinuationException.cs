namespace A2A;

/// <summary>The stage or precondition that prevented successful proactive continuation.</summary>
public enum TaskContinuationFailureKind
{
    /// <summary>The requested task does not exist.</summary>
    TaskNotFound,
    /// <summary>No active push registration exists for the requested task.</summary>
    MissingPushRegistration,
    /// <summary>The task is already terminal.</summary>
    NonContinuableTask,
    /// <summary>The producer failed.</summary>
    Producer,
    /// <summary>Reading or persisting task state failed.</summary>
    TaskStore,
    /// <summary>Reading push registrations failed.</summary>
    PushNotificationStore,
    /// <summary>Projecting an event failed.</summary>
    Projection,
    /// <summary>An event has an invalid payload or task/context identity.</summary>
    InvalidEvent,
    /// <summary>Cancellation or server shutdown stopped the operation.</summary>
    Cancellation,
    /// <summary>Push admission failed, or a persisted event had no active delivery route.</summary>
    PushAdmission,
    /// <summary>The producer completed without persisting any events.</summary>
    NoEvents,
}

/// <summary>A continuation failure with explicit committed progress, without rollback semantics.</summary>
/// <remarks>
/// Cancellation is reported with <see cref="TaskContinuationFailureKind.Cancellation"/> and an
/// <see cref="OperationCanceledException"/> as the inner exception. Progress remains available.
/// </remarks>
public sealed class TaskContinuationException : Exception
{
    internal TaskContinuationException(TaskContinuationFailureKind failureKind, string message,
        TaskContinuationResult? progress, Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
        Progress = progress;
    }

    /// <summary>Gets the failed stage or precondition.</summary>
    public TaskContinuationFailureKind FailureKind { get; }

    /// <summary>Gets committed progress, or null if the task snapshot could not be loaded.</summary>
    public TaskContinuationResult? Progress { get; }
}

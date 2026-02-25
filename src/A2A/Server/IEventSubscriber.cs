namespace A2A;

/// <summary>
/// Subscribes to events for a task using catch-up-then-live pattern.
/// Separate from <see cref="IEventStore"/> because notification/pub-sub
/// is an orthogonal concern from durable persistence.
/// </summary>
public interface IEventSubscriber
{
    /// <summary>
    /// Subscribe to events for a task.
    /// Returns historical events from afterVersion, then live events as they arrive.
    /// The enumerable completes when the task reaches a terminal state or cancellation fires.
    /// </summary>
    /// <param name="taskId">The task to subscribe to.</param>
    /// <param name="afterVersion">Position after which to receive events. Use -1 for all events.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<EventEnvelope> SubscribeAsync(
        string taskId,
        long afterVersion = -1,
        CancellationToken cancellationToken = default);
}

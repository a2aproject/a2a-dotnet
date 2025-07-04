using System.Collections.Concurrent;

namespace A2A;

/// <summary>
/// Provides an asynchronous enumerable for task update events, allowing real-time streaming of A2A events.
/// </summary>
public class TaskUpdateEventEnumerator : IAsyncEnumerable<A2AEvent>
{
    private bool isFinal;
    private readonly ConcurrentQueue<A2AEvent> _UpdateEvents = new();
    private readonly SemaphoreSlim _semaphore = new(0);

    /// <summary>
    /// Gets or sets the processing task to prevent garbage collection.
    /// </summary>
    public Task? ProcessingTask { get; set; } // Store the processing task so it doesn't get garbage collected

    /// <summary>
    /// Notifies the enumerator of a new task update event.
    /// </summary>
    /// <param name="taskUpdateEvent">The task update event to notify.</param>
    public void NotifyEvent(A2AEvent taskUpdateEvent)
    {
        // Enqueue the event to the queue
        _UpdateEvents.Enqueue(taskUpdateEvent);
        _semaphore.Release();
    }

    /// <summary>
    /// Notifies the enumerator of a final task update event, indicating the end of the stream.
    /// </summary>
    /// <param name="taskUpdateEvent">The final task update event to notify.</param>
    public void NotifyFinalEvent(A2AEvent taskUpdateEvent)
    {
        isFinal = true;
        // Enqueue the final event to the queue
        _UpdateEvents.Enqueue(taskUpdateEvent);
        _semaphore.Release();
    }

    /// <inheritdoc />
    public async IAsyncEnumerator<A2AEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        while (!isFinal || !_UpdateEvents.IsEmpty)
        {
            // Wait for an event to be available
            await _semaphore.WaitAsync(cancellationToken);
            if (_UpdateEvents.TryDequeue(out var taskUpdateEvent))
            {
                yield return taskUpdateEvent;
            }
        }
    }
}
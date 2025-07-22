using System.Collections.Concurrent;

namespace A2A;

/// <summary>
/// Enumerator for streaming task update events to clients.
/// </summary>
public sealed class TaskUpdateEventEnumerator : IAsyncEnumerable<A2AEvent>, IDisposable, IAsyncDisposable
{
    private bool isFinal;
    private readonly ConcurrentQueue<A2AEvent> _UpdateEvents = new();
    private readonly SemaphoreSlim _semaphore = new(0);

    /// <summary>
    /// Gets or sets the processing task to prevent garbage collection.
    /// </summary>
    public Task? ProcessingTask { get; set; }

    /// <summary>
    /// Notifies of a new event in the task stream.
    /// </summary>
    /// <param name="taskUpdateEvent">The event to notify.</param>
    public void NotifyEvent(A2AEvent taskUpdateEvent)
    {
        if (taskUpdateEvent is null)
        {
            throw new ArgumentNullException(nameof(taskUpdateEvent));
        }

        // Enqueue the event to the queue
        _UpdateEvents.Enqueue(taskUpdateEvent);
        _semaphore.Release();
    }

    /// <summary>
    /// Notifies of the final event in the task stream.
    /// </summary>
    /// <param name="taskUpdateEvent">The final event to notify.</param>
    public void NotifyFinalEvent(A2AEvent taskUpdateEvent)
    {
        if (taskUpdateEvent is null)
        {
            throw new ArgumentNullException(nameof(taskUpdateEvent));
        }

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
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (_UpdateEvents.TryDequeue(out var taskUpdateEvent))
            {
                yield return taskUpdateEvent;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _semaphore.Dispose();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask;
    }
}
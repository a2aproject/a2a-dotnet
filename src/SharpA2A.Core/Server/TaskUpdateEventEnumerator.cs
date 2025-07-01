
using System.Collections.Concurrent;

namespace A2A.Core;

public class TaskUpdateEventEnumerator : IAsyncEnumerable<A2AEvent>
{
    private bool isFinal = false;
    private ConcurrentQueue<A2AEvent> _UpdateEvents = new ConcurrentQueue<A2AEvent>();
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(0);
    public Task? ProcessingTask { get; set; } // Store the processing task so it doesn't get garbage collected

    public void NotifyEvent(A2AEvent taskUpdateEvent)
    {
        // Enqueue the event to the queue
        _UpdateEvents.Enqueue(taskUpdateEvent);
        _semaphore.Release();
    }

    public void NotifyFinalEvent(A2AEvent taskUpdateEvent)
    {
        isFinal = true;
        // Enqueue the final event to the queue
        _UpdateEvents.Enqueue(taskUpdateEvent);
        _semaphore.Release();
    }

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


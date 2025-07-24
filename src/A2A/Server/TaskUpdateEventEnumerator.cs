using System.Threading.Channels;

using System.Collections.Concurrent;

namespace A2A;

/// <summary>
/// Enumerator for streaming task update events to clients.
/// </summary>
public sealed class TaskUpdateEventEnumerator : IAsyncEnumerable<A2AEvent>, IDisposable, IAsyncDisposable
{
    private readonly Channel<A2AEvent> _channel = Channel.CreateUnbounded<A2AEvent>();

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

        if (!_channel.Writer.TryWrite(taskUpdateEvent))
        {
            throw new InvalidOperationException("Unable to write to the event channel.");
        }
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

        if (!_channel.Writer.TryWrite(taskUpdateEvent))
        {
            throw new InvalidOperationException("Unable to write to the event channel.");
        }

        _channel.Writer.TryComplete();
    }

    /// <inheritdoc />
    public IAsyncEnumerator<A2AEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default) => _channel.Reader.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        _channel.Writer.TryComplete();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        this.Dispose();
        await Task.CompletedTask;
    }
}
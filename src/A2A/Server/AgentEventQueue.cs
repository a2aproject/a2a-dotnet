using System.Threading.Channels;

namespace A2A;

/// <summary>
/// A write-only event channel for agent code to produce A2A response events.
/// Backed by <see cref="Channel{T}"/>. Implements <see cref="IAsyncEnumerable{T}"/>
/// for consumption by <see cref="A2AServer"/> and protocol processors.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Queue accurately describes the channel-backed event queue semantics")]
public sealed class AgentEventQueue : IAsyncEnumerable<StreamResponse>
{
    private readonly Channel<StreamResponse> _channel;

    /// <summary>Creates a bounded event queue (default capacity 16).</summary>
    /// <param name="capacity">Maximum number of buffered events.</param>
    public AgentEventQueue(int capacity = 16)
    {
        _channel = Channel.CreateBounded<StreamResponse>(
            new BoundedChannelOptions(capacity)
            {
                SingleWriter = false,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
    }

    /// <summary>Creates an event queue with custom channel options.</summary>
    /// <param name="options">Channel configuration options.</param>
    public AgentEventQueue(BoundedChannelOptions options)
    {
        _channel = Channel.CreateBounded<StreamResponse>(options);
    }

    /// <summary>Write a raw StreamResponse (low-level).</summary>
    /// <param name="response">The response event to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask WriteAsync(StreamResponse response, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(response, cancellationToken);

    /// <summary>Enqueue a task as the initial response event.</summary>
    /// <param name="task">The task to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask EnqueueTaskAsync(AgentTask task, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(new StreamResponse { Task = task }, cancellationToken);

    /// <summary>Enqueue a message response (task-free mode).</summary>
    /// <param name="message">The message to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask EnqueueMessageAsync(Message message, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(new StreamResponse { Message = message }, cancellationToken);

    /// <summary>Enqueue a task status update event.</summary>
    /// <param name="update">The status update event to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask EnqueueStatusUpdateAsync(TaskStatusUpdateEvent update, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(new StreamResponse { StatusUpdate = update }, cancellationToken);

    /// <summary>Enqueue a task artifact update event.</summary>
    /// <param name="update">The artifact update event to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask EnqueueArtifactUpdateAsync(TaskArtifactUpdateEvent update, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(new StreamResponse { ArtifactUpdate = update }, cancellationToken);

    /// <summary>Signal completion of the event stream.</summary>
    /// <param name="exception">Optional exception to signal failure.</param>
    public void Complete(Exception? exception = null)
        => _channel.Writer.TryComplete(exception);

    /// <inheritdoc />
    public async IAsyncEnumerator<StreamResponse> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }
}

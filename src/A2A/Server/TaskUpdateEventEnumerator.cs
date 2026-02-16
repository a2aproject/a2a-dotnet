using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace A2A;

/// <summary>Provides an async enumerable of <see cref="StreamResponse"/> backed by a channel.</summary>
public sealed class TaskUpdateEventEnumerator : IAsyncEnumerable<StreamResponse>
{
    private readonly Channel<StreamResponse> _channel;

    /// <summary>Initializes a new instance of the <see cref="TaskUpdateEventEnumerator"/> class.</summary>
    /// <param name="options">Optional bounded channel options.</param>
    public TaskUpdateEventEnumerator(BoundedChannelOptions? options = null)
    {
        _channel = options is not null
            ? Channel.CreateBounded<StreamResponse>(options)
            : Channel.CreateUnbounded<StreamResponse>();
    }

    /// <summary>Writes a stream response to the channel.</summary>
    /// <param name="response">The stream response to write.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask WriteAsync(StreamResponse response, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Marks the channel as complete, indicating no more items will be written.</summary>
    /// <param name="exception">An optional exception to signal an error.</param>
    public void Complete(Exception? exception = null)
    {
        _channel.Writer.TryComplete(exception);
    }

    /// <inheritdoc />
    public async IAsyncEnumerator<StreamResponse> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
}
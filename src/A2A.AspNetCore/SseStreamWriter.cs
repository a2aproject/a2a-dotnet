using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Globalization;
using System.IO.Pipelines;
using System.Text;

namespace A2A.AspNetCore;

/// <summary>
/// Writes Server-Sent Events (SSE) frames with periodic keep-alive comment frames and
/// monotonically increasing event ids.
/// </summary>
/// <remarks>
/// <para>Keep-alive comment frames (<c>: keep-alive</c>) prevent proxies and load balancers
/// from terminating idle SSE connections, and event ids let clients resume subscriptions
/// from a known point.</para>
/// <para>All writes are serialized through a single lock, so the heartbeat task and the
/// event writer can never interleave frames.</para>
/// </remarks>
internal sealed class SseStreamWriter : IAsyncDisposable
{
    private static readonly TimeSpan DefaultKeepAliveInterval = TimeSpan.FromSeconds(15);

    private readonly PipeWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _heartbeatCts;
    private readonly Task _heartbeatTask;
    private long _eventId;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance and starts the keep-alive heartbeat.
    /// </summary>
    /// <param name="httpContext">The HTTP context to write the SSE stream to.</param>
    /// <param name="keepAliveInterval">Optional heartbeat interval; defaults to 15 seconds.</param>
    public SseStreamWriter(HttpContext httpContext, TimeSpan? keepAliveInterval = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        _writer = httpContext.Features.GetRequiredFeature<IHttpResponseBodyFeature>().Writer;
        _heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted);
        var interval = keepAliveInterval ?? DefaultKeepAliveInterval;

        _heartbeatTask = Task.Run(async () =>
        {
            try
            {
                while (!_heartbeatCts.IsCancellationRequested)
                {
                    await Task.Delay(interval, _heartbeatCts.Token).ConfigureAwait(false);
                    await WriteFrameAsync(": keep-alive\n\n", _heartbeatCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Stream completed or client disconnected — expected
            }
            catch
            {
                // Response body no longer writable — stop heartbeating
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Writes a single SSE event frame: <c>id: {n}</c> followed by the <c>data:</c> payload.
    /// </summary>
    /// <param name="dataJson">The JSON payload for the event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WriteEventAsync(string dataJson, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _eventId);
        await WriteFrameAsync(
            $"id: {id.ToString(CultureInfo.InvariantCulture)}\ndata: {dataJson}\n\n",
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _heartbeatCts.CancelAsync().ConfigureAwait(false);

#pragma warning disable VSTHRD003 // Intentional: the heartbeat task was started in the constructor and is owned by this instance
        try { await _heartbeatTask.ConfigureAwait(false); }
        catch { /* Already cancelled */ }
#pragma warning restore VSTHRD003

        _heartbeatCts.Dispose();
        _writeLock.Dispose();
    }

    private async Task WriteFrameAsync(string frame, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bytes = Encoding.UTF8.GetBytes(frame);
            await _writer.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}

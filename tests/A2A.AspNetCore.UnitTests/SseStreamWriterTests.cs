using Microsoft.AspNetCore.Http;
using System.Text;

namespace A2A.AspNetCore.Tests;

public class SseStreamWriterTests
{
    [Fact]
    public async Task WriteEventAsync_EmitsMonotonicEventIdsBeforeData()
    {
        // Arrange
        var context = CreateHttpContext();
        await using var writer = new SseStreamWriter(context, TimeSpan.FromSeconds(60));

        // Act
        await writer.WriteEventAsync("{\"a\":1}", CancellationToken.None);
        await writer.WriteEventAsync("{\"b\":2}", CancellationToken.None);

        // Assert — each data frame is preceded by an incrementing "id:" field
        var body = GetResponseBody(context);
        Assert.Contains("id: 1\ndata: {\"a\":1}\n\n", body);
        Assert.Contains("id: 2\ndata: {\"b\":2}\n\n", body);
    }

    [Fact]
    public async Task Heartbeat_EmitsKeepAliveCommentFrames()
    {
        // Arrange
        var responseBody = new HeartbeatObservingStream();
        var context = CreateHttpContext(responseBody);
        var writer = new SseStreamWriter(context, TimeSpan.FromMilliseconds(50));

        // Act
        await writer.WriteEventAsync("{\"x\":1}", CancellationToken.None);
        await responseBody.HeartbeatWritten.WaitAsync(TimeSpan.FromSeconds(5));
        await writer.DisposeAsync();

        // Assert
        var body = GetResponseBody(context);
        Assert.Contains(": keep-alive\n\n", body);
    }

    [Fact]
    public async Task Dispose_StopsHeartbeat()
    {
        // Arrange
        var context = CreateHttpContext();
        var writer = new SseStreamWriter(context, TimeSpan.FromMilliseconds(30));
        await writer.DisposeAsync();

        // Act — capture the body right after dispose, then wait past several ticks
        var bodyAfterDispose = GetResponseBody(context);
        await Task.Delay(150);

        // Assert — no heartbeat frames appeared after disposal
        var bodyLater = GetResponseBody(context);
        Assert.Equal(bodyAfterDispose, bodyLater);
        Assert.DoesNotContain(": keep-alive", bodyAfterDispose);
    }

    // --- Helpers ---

    private static DefaultHttpContext CreateHttpContext(Stream? responseBody = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = responseBody ?? new MemoryStream();
        return context;
    }

    private static string GetResponseBody(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private sealed class HeartbeatObservingStream : MemoryStream
    {
        private readonly TaskCompletionSource _heartbeatWritten =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task HeartbeatWritten => _heartbeatWritten.Task;

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer, cancellationToken);

            if (Encoding.UTF8.GetString(buffer.Span).Contains(": keep-alive\n\n", StringComparison.Ordinal))
            {
                _heartbeatWritten.TrySetResult();
            }
        }
    }
}

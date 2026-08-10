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
        // Arrange — short heartbeat interval so the test doesn't wait long (BUG-09)
        var context = CreateHttpContext();
        var writer = new SseStreamWriter(context, TimeSpan.FromMilliseconds(50));

        // Act — write one event, then wait long enough for several heartbeat ticks
        await writer.WriteEventAsync("{\"x\":1}", CancellationToken.None);
        await Task.Delay(300);
        await writer.DisposeAsync();

        // Assert — keep-alive comment frames were written
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

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string GetResponseBody(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }
}

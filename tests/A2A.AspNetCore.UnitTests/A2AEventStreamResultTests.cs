using Microsoft.AspNetCore.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace A2A.AspNetCore.Tests;

public class A2AEventStreamResultTests
{
    [Fact]
    public async Task ExecuteAsync_A2AException_PreservesErrorCodeAndMessage()
    {
        // Arrange — A2A-specific error must keep code, message, and structured data
        var events = ThrowingAsyncEnumerable(new A2AException("Task not found", A2AErrorCode.TaskNotFound));
        var result = new A2AEventStreamResult(events);
        var httpContext = CreateHttpContext();

        // Act
        await result.ExecuteAsync(httpContext);

        // Assert
        var body = GetResponseBody(httpContext);
        using var doc = JsonDocument.Parse(ExtractErrorDataLine(body));
        var error = doc.RootElement.GetProperty("error");

        Assert.Equal((int)A2AErrorCode.TaskNotFound, error.GetProperty("code").GetInt32());
        Assert.Equal("Task not found", error.GetProperty("message").GetString());

        // A2A-specific codes carry google.rpc ErrorInfo data (same as JSON-RPC transport)
        var data = error.GetProperty("data");
        Assert.Equal("TASK_NOT_FOUND", data[0].GetProperty("reason").GetString());
        Assert.Equal("a2a-protocol.org", data[0].GetProperty("domain").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_GenericException_ReturnsInternalError_WithoutLeakingMessage()
    {
        // Arrange
        var events = ThrowingAsyncEnumerable(new InvalidOperationException("sensitive internal details"));
        var result = new A2AEventStreamResult(events);
        var httpContext = CreateHttpContext();

        // Act
        await result.ExecuteAsync(httpContext);

        // Assert — falls back to -32603 with a generic message, never leaks internals
        var body = GetResponseBody(httpContext);
        using var doc = JsonDocument.Parse(ExtractErrorDataLine(body));
        var error = doc.RootElement.GetProperty("error");

        Assert.Equal((int)A2AErrorCode.InternalError, error.GetProperty("code").GetInt32());
        Assert.Equal("An internal error occurred during streaming.", error.GetProperty("message").GetString());
        Assert.DoesNotContain("sensitive internal details", body);
    }

    [Fact]
    public async Task ExecuteAsync_OperationCanceledException_WritesNoErrorEvent()
    {
        // Arrange
        var events = ThrowingAsyncEnumerable(new OperationCanceledException());
        var result = new A2AEventStreamResult(events);
        var httpContext = CreateHttpContext();

        // Act
        await result.ExecuteAsync(httpContext);

        // Assert — body contains no error SSE data line
        var body = GetResponseBody(httpContext);
        Assert.DoesNotContain("\"error\"", body);
    }

    [Fact]
    public async Task ExecuteAsync_SetsCorrectResponseHeaders()
    {
        // Arrange
        var events = ThrowingAsyncEnumerable(new A2AException("test", A2AErrorCode.InternalError));
        var result = new A2AEventStreamResult(events);
        var httpContext = CreateHttpContext();

        // Act
        await result.ExecuteAsync(httpContext);

        // Assert
        Assert.Equal("text/event-stream", httpContext.Response.ContentType);
        Assert.Equal("no-cache,no-store", httpContext.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_DataEvents_AreValidSseFrames()
    {
        // Arrange
        var result = new A2AEventStreamResult(DataAsyncEnumerable());
        var httpContext = CreateHttpContext();

        // Act
        await result.ExecuteAsync(httpContext);

        // Assert — regular stream events are still bare "data: {StreamResponse}" frames
        var body = GetResponseBody(httpContext);
        Assert.Contains("data: {", body);
        Assert.DoesNotContain("\"error\"", body);
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
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string ExtractErrorDataLine(string body)
    {
        var lines = body.Split('\n');
        var dataLine = lines.FirstOrDefault(l => l.StartsWith("data: ", StringComparison.Ordinal) && l.Contains("\"error\""))
            ?? throw new InvalidOperationException($"No SSE data line with error found in response body:\n{body}");
        return dataLine["data: ".Length..];
    }

    private static async IAsyncEnumerable<StreamResponse> ThrowingAsyncEnumerable(
        Exception exception, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // force async state machine
        throw exception;
#pragma warning disable CS0162 // Unreachable code — required to satisfy IAsyncEnumerable<T>
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<StreamResponse> DataAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield return new StreamResponse
        {
            Task = new AgentTask
            {
                Id = "t1",
                ContextId = "c1",
                Status = new TaskStatus { State = TaskState.Working },
            },
        };
    }
}

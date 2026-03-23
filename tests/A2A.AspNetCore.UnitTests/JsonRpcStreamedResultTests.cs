using Microsoft.AspNetCore.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace A2A.AspNetCore.Tests;

public class JsonRpcStreamedResultTests
{
    [Fact]
    public async Task ExecuteAsync_A2AException_PreservesErrorCode()
    {
        // Arrange
        var requestId = new JsonRpcId("req-1");
        var errorCode = A2AErrorCode.TaskNotFound;
        var errorMessage = "The specified task does not exist.";

        var events = ThrowingAsyncEnumerable(new A2AException(errorMessage, errorCode));
        var result = new JsonRpcStreamedResult(events, requestId);
        var httpContext = CreateHttpContext();

        // Act
        await result.ExecuteAsync(httpContext);

        // Assert
        var body = GetResponseBody(httpContext);
        var response = ParseSseDataLine(body);

        Assert.NotNull(response.Error);
        Assert.Equal((int)errorCode, response.Error.Code);
        Assert.Equal(errorMessage, response.Error.Message);
        Assert.Equal("req-1", response.Id.AsString());
    }

    [Fact]
    public async Task ExecuteAsync_A2AExceptionMethodNotFound_PreservesErrorCode()
    {
        // Arrange
        var requestId = new JsonRpcId("req-2");
        var events = ThrowingAsyncEnumerable(
            new A2AException("Method not found", A2AErrorCode.MethodNotFound));
        var result = new JsonRpcStreamedResult(events, requestId);
        var httpContext = CreateHttpContext();

        // Act
        await result.ExecuteAsync(httpContext);

        // Assert
        var body = GetResponseBody(httpContext);
        var response = ParseSseDataLine(body);

        Assert.NotNull(response.Error);
        Assert.Equal((int)A2AErrorCode.MethodNotFound, response.Error.Code);
        Assert.Equal("Method not found", response.Error.Message);
    }

    [Fact]
    public async Task ExecuteAsync_GenericException_ReturnsInternalError()
    {
        // Arrange
        var requestId = new JsonRpcId("req-3");
        var events = ThrowingAsyncEnumerable(new InvalidOperationException("sensitive internal details"));
        var result = new JsonRpcStreamedResult(events, requestId);
        var httpContext = CreateHttpContext();

        // Act
        await result.ExecuteAsync(httpContext);

        // Assert
        var body = GetResponseBody(httpContext);
        var response = ParseSseDataLine(body);

        Assert.NotNull(response.Error);
        Assert.Equal((int)A2AErrorCode.InternalError, response.Error.Code);
        // Must NOT leak the original exception message
        Assert.DoesNotContain("sensitive internal details", response.Error.Message);
        Assert.Equal("An internal error occurred during streaming.", response.Error.Message);
    }

    [Fact]
    public async Task ExecuteAsync_OperationCanceledException_WritesNoErrorEvent()
    {
        // Arrange
        var requestId = new JsonRpcId("req-4");
        var events = ThrowingAsyncEnumerable(new OperationCanceledException());
        var result = new JsonRpcStreamedResult(events, requestId);
        var httpContext = CreateHttpContext();

        // Act
        await result.ExecuteAsync(httpContext);

        // Assert — body should contain no error SSE data line
        var body = GetResponseBody(httpContext);
        Assert.DoesNotContain("\"error\"", body);
    }

    [Fact]
    public async Task ExecuteAsync_SetsCorrectResponseHeaders()
    {
        // Arrange
        var requestId = new JsonRpcId("req-5");
        var events = ThrowingAsyncEnumerable(new A2AException("test", A2AErrorCode.InternalError));
        var result = new JsonRpcStreamedResult(events, requestId);
        var httpContext = CreateHttpContext();

        // Act
        await result.ExecuteAsync(httpContext);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal("text/event-stream", httpContext.Response.ContentType);
        Assert.Equal("no-cache", httpContext.Response.Headers["Cache-Control"].ToString());
    }

    [Fact]
    public async Task ExecuteAsync_ErrorResponseIsValidSseFormat()
    {
        // Arrange
        var requestId = new JsonRpcId("req-6");
        var events = ThrowingAsyncEnumerable(
            new A2AException("Bad params", A2AErrorCode.InvalidParams));
        var result = new JsonRpcStreamedResult(events, requestId);
        var httpContext = CreateHttpContext();

        // Act
        await result.ExecuteAsync(httpContext);

        // Assert — the error line should be a valid "data: {json}\n\n" SSE frame
        var body = GetResponseBody(httpContext);
        var lines = body.Split('\n');
        var dataLine = lines.FirstOrDefault(l => l.StartsWith("data: ", StringComparison.Ordinal) && l.Contains("\"error\""));
        Assert.NotNull(dataLine);
        var json = dataLine["data: ".Length..];
        var doc = JsonDocument.Parse(json);
        Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal((int)A2AErrorCode.InvalidParams, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void Constructor_NullEvents_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new JsonRpcStreamedResult(null!, new JsonRpcId("x")));
    }

    [Fact]
    public async Task ExecuteAsync_NullHttpContext_Throws()
    {
        var events = EmptyAsyncEnumerable();
        var result = new JsonRpcStreamedResult(events, new JsonRpcId("x"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => result.ExecuteAsync(null!));
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

    private static JsonRpcResponse ParseSseDataLine(string body)
    {
        // Find the last "data: " line that contains error info
        var lines = body.Split('\n');
        var dataLine = lines.LastOrDefault(l => l.StartsWith("data: ", StringComparison.Ordinal) && l.Contains("\"error\""))
            ?? throw new InvalidOperationException(
                $"No SSE data line with error found in response body:\n{body}");
        var json = dataLine["data: ".Length..];
        return JsonSerializer.Deserialize<JsonRpcResponse>(json, A2AJsonUtilities.DefaultOptions)
            ?? throw new InvalidOperationException("Failed to deserialize JsonRpcResponse");
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

    private static async IAsyncEnumerable<StreamResponse> EmptyAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}

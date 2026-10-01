using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.AspNetCore.Tests;

public partial class A2AEventStreamResultTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_ErrorMapperFailureDoesNotRecordSecretException(bool ownsOperation)
    {
        const string secret = "secret-payload-token-feature";
        using var ambient = new Activity("caller").Start();
        using var operation = ownsOperation ? new Activity("a2a.operation").Start() : null;
        var result = new A2AEventStreamResult<SerializationEvent>(
            SerializationEventsAsync(new SerializationEvent("stream failed")),
            EventStreamJsonContext.Default.SerializationEvent,
            _ => throw new InvalidOperationException(secret),
            operation);
        var context = CreateHttpContext();

        await result.ExecuteAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.DoesNotContain(secret, GetResponseBody(context));
        Assert.Empty(ambient.Events);
        Assert.Empty(ambient.TagObjects);
        Assert.Equal(ActivityStatusCode.Unset, ambient.Status);
        if (operation is not null)
        {
            Assert.Equal("error", operation.GetTagItem("a2a.operation.outcome"));
            Assert.Equal(ActivityStatusCode.Error, operation.Status);
            Assert.Empty(operation.Events);
            Assert.Null(operation.StatusDescription);
        }
    }

    [Theory]
    [InlineData(A2AErrorCode.TaskNotFound, StatusCodes.Status404NotFound)]
    [InlineData(A2AErrorCode.UnsupportedOperation, StatusCodes.Status400BadRequest)]
    public async Task ExecuteAsync_A2AExceptionBeforeFirstEvent_ReturnsRestError(
        A2AErrorCode errorCode, int expectedStatusCode)
    {
        var result = new A2AEventStreamResult(
            ThrowingAsyncEnumerable(new A2AException("Subscription rejected.", errorCode)));
        var httpContext = CreateHttpContext();

        await result.ExecuteAsync(httpContext);

        Assert.Equal(expectedStatusCode, httpContext.Response.StatusCode);
        Assert.Equal("application/json", httpContext.Response.ContentType);

        using var body = JsonDocument.Parse(GetResponseBody(httpContext));
        var error = body.RootElement.GetProperty("error");
        Assert.Equal("Subscription rejected.", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_GenericExceptionBeforeFirstEvent_ReturnsInternalError()
    {
        var result = new A2AEventStreamResult(
            ThrowingAsyncEnumerable(new InvalidOperationException("sensitive details")));
        var httpContext = CreateHttpContext();

        await result.ExecuteAsync(httpContext);

        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        Assert.Equal("application/json", httpContext.Response.ContentType);
        Assert.DoesNotContain("sensitive details", GetResponseBody(httpContext));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_UncancelledCancellationBeforeFirstEvent_ReturnsInternalError(
        bool timeout)
    {
        var result = new A2AEventStreamResult(
            ThrowingAsyncEnumerable(CreateUncancelledCancellation(timeout)));
        var httpContext = CreateHttpContext();

        await result.ExecuteAsync(httpContext);

        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        Assert.Equal("application/json", httpContext.Response.ContentType);
        Assert.Contains("An internal error occurred.", GetResponseBody(httpContext));
    }

    [Fact]
    public async Task ExecuteAsync_RequestAbortedBeforeFirstEvent_WritesNoError()
    {
        using var cancellation = new CancellationTokenSource();
        var result = new A2AEventStreamResult(
            CancelAndThrowAsyncEnumerable(cancellation, yieldEvent: false));
        var httpContext = CreateHttpContext();
        httpContext.RequestAborted = cancellation.Token;

        await result.ExecuteAsync(httpContext);

        Assert.Empty(GetResponseBody(httpContext));
    }

    [Fact]
    public async Task ExecuteAsync_MultipleEvents_EmitsSseEventsOnceAndInOrder()
    {
        var result = new A2AEventStreamResult(MultipleEventsAsyncEnumerable());
        var httpContext = CreateHttpContext();

        await result.ExecuteAsync(httpContext);

        var body = GetResponseBody(httpContext);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal("text/event-stream", httpContext.Response.ContentType);
        Assert.Equal("no-cache,no-store", httpContext.Response.Headers.CacheControl);
        Assert.Equal(1, body.Split("\"task-1\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, body.Split("\"task-2\"", StringSplitOptions.None).Length - 1);
        Assert.True(
            body.IndexOf("\"task-1\"", StringComparison.Ordinal) <
            body.IndexOf("\"task-2\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_EmptyStream_ReturnsSseResponse()
    {
        var result = new A2AEventStreamResult(EmptyAsyncEnumerable());
        var httpContext = CreateHttpContext();

        await result.ExecuteAsync(httpContext);

        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal("text/event-stream", httpContext.Response.ContentType);
        Assert.Empty(GetResponseBody(httpContext));
    }

    [Fact]
    public async Task ExecuteAsync_ExceptionAfterFirstEvent_ReturnsSseError()
    {
        var result = new A2AEventStreamResult(
            YieldThenThrowAsyncEnumerable(new A2AException("Subscription failed.", A2AErrorCode.InvalidRequest)));
        var httpContext = CreateHttpContext();

        await result.ExecuteAsync(httpContext);

        var body = GetResponseBody(httpContext);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal("text/event-stream", httpContext.Response.ContentType);
        Assert.Contains("\"task-1\"", body);
        Assert.Contains("An internal error occurred during streaming.", body);
        Assert.DoesNotContain("Subscription failed.", body);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_UncancelledCancellationAfterFirstEvent_ReturnsSseError(
        bool timeout)
    {
        var result = new A2AEventStreamResult(
            YieldThenThrowAsyncEnumerable(CreateUncancelledCancellation(timeout)));
        var httpContext = CreateHttpContext();

        await result.ExecuteAsync(httpContext);

        var body = GetResponseBody(httpContext);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal("text/event-stream", httpContext.Response.ContentType);
        Assert.Contains("\"task-1\"", body);
        Assert.Contains("An internal error occurred during streaming.", body);
    }

    [Fact]
    public async Task ExecuteAsync_RequestAbortedAfterFirstEvent_WritesNoError()
    {
        using var cancellation = new CancellationTokenSource();
        var result = new A2AEventStreamResult(
            CancelAndThrowAsyncEnumerable(cancellation, yieldEvent: true));
        var httpContext = CreateHttpContext();
        httpContext.RequestAborted = cancellation.Token;

        await result.ExecuteAsync(httpContext);

        var body = GetResponseBody(httpContext);
        Assert.Contains("\"task-1\"", body);
        Assert.DoesNotContain("\"error\"", body);
    }

    [Fact]
    public async Task ExecuteAsync_FirstEventSerializationFailure_ReturnsInternalRestError()
    {
        var result = new A2AEventStreamResult<SerializationEvent>(
            SerializationEventsAsync(new SerializationEvent("secret failure")),
            EventStreamJsonContext.Default.SerializationEvent,
            static _ => new A2AErrorResult(
                new A2AException(
                    "An internal error occurred.",
                    A2AErrorCode.InternalError)),
            operationActivity: null);
        var httpContext = CreateHttpContext();

        await result.ExecuteAsync(httpContext);

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            httpContext.Response.StatusCode);
        Assert.Equal("application/json", httpContext.Response.ContentType);
        var body = GetResponseBody(httpContext);
        Assert.Contains("An internal error occurred.", body);
        Assert.DoesNotContain("streaming", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret failure", body);
    }

    [Fact]
    public async Task ExecuteAsync_LaterEventSerializationFailure_ReturnsSseError()
    {
        var result = new A2AEventStreamResult<SerializationEvent>(
            SerializationEventsAsync(
                new SerializationEvent(value: "event-1"),
                new SerializationEvent("secret failure")),
            EventStreamJsonContext.Default.SerializationEvent,
            static _ => new A2AErrorResult(
                new A2AException(
                    "An internal error occurred.",
                    A2AErrorCode.InternalError)),
            operationActivity: null);
        var httpContext = CreateHttpContext();

        await result.ExecuteAsync(httpContext);

        var body = GetResponseBody(httpContext);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal("text/event-stream", httpContext.Response.ContentType);
        Assert.Contains("\"value\":\"event-1\"", body);
        Assert.Contains("An internal error occurred during streaming.", body);
        Assert.DoesNotContain("secret failure", body);
    }

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

    private static async IAsyncEnumerable<StreamResponse> ThrowingAsyncEnumerable(
        Exception exception, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        throw exception;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<StreamResponse> CancelAndThrowAsyncEnumerable(
        CancellationTokenSource cancellation,
        bool yieldEvent,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (yieldEvent)
        {
            yield return CreateTaskResponse("task-1");
        }

        await cancellation.CancelAsync();
        throw new OperationCanceledException(cancellation.Token);
    }

    private static async IAsyncEnumerable<StreamResponse> EmptyAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<StreamResponse> MultipleEventsAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield return CreateTaskResponse("task-1");
        yield return CreateTaskResponse("task-2");
    }

    private static async IAsyncEnumerable<StreamResponse> YieldThenThrowAsyncEnumerable(
        Exception exception, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return CreateTaskResponse("task-1");
        await Task.CompletedTask;
        throw exception;
    }

    private static async IAsyncEnumerable<SerializationEvent> SerializationEventsAsync(
        params SerializationEvent[] events)
    {
        foreach (var streamEvent in events)
        {
            yield return streamEvent;
        }

        await Task.CompletedTask;
    }

    private static StreamResponse CreateTaskResponse(string taskId) =>
        new()
        {
            Task = new AgentTask
            {
                Id = taskId,
                ContextId = "context-1",
                Status = new TaskStatus { State = TaskState.Working },
            },
        };

    private static Exception CreateUncancelledCancellation(bool timeout) =>
        timeout
            ? new TaskCanceledException(
                "Timed out.",
                new TimeoutException("Timed out."),
                CancellationToken.None)
            : new OperationCanceledException("Cancelled.", CancellationToken.None);

    private sealed class SerializationEvent(string? failureMessage = null, string? value = null)
    {
        [JsonPropertyName("value")]
        public string Value => failureMessage is null
            ? value!
            : throw new InvalidOperationException(failureMessage);
    }

    [JsonSerializable(typeof(SerializationEvent))]
    private sealed partial class EventStreamJsonContext : JsonSerializerContext;
}

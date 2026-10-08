using Microsoft.AspNetCore.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using V03 = A2A.V0_3;

namespace A2A.V0_3Compat.UnitTests;

public class V03JsonRpcStreamedResultTests
{
    [Fact]
    public async Task ExecuteAsync_A2AExceptionBeforeFirstEvent_ReturnsJsonRpcError()
    {
        // Arrange
        var events = ThrowingAsyncEnumerable(
            new A2AException("Task not found.", A2AErrorCode.TaskNotFound));
        var result = new V03JsonRpcStreamedResult(events, new V03.JsonRpcId("req-1"));
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        // Act
        await result.ExecuteAsync(httpContext);

        // Assert
        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        var response = JsonSerializer.Deserialize<V03.JsonRpcResponse>(
            body, V03.A2AJsonUtilities.DefaultOptions);

        Assert.Equal("application/json", httpContext.Response.ContentType);
        Assert.Equal((int)V03.A2AErrorCode.TaskNotFound, response?.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_DisposeAsyncThrowsBeforeFirstEvent_ReturnsJsonRpcError()
    {
        // Arrange
        var result = new V03JsonRpcStreamedResult(
            new EmptyThrowingDisposeAsyncEnumerable(), new V03.JsonRpcId("req-2"));
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        // Act
        await result.ExecuteAsync(httpContext);

        // Assert
        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        var response = JsonSerializer.Deserialize<V03.JsonRpcResponse>(
            body, V03.A2AJsonUtilities.DefaultOptions);

        Assert.Equal("application/json", httpContext.Response.ContentType);
        Assert.Equal((int)V03.A2AErrorCode.InternalError, response?.Error?.Code);
    }

    private static async IAsyncEnumerable<V03.A2AEvent> ThrowingAsyncEnumerable(
        Exception exception, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        throw exception;
#pragma warning disable CS0162 // Unreachable code required for an async enumerable
        yield break;
#pragma warning restore CS0162
    }

    private sealed class EmptyThrowingDisposeAsyncEnumerable :
        IAsyncEnumerable<V03.A2AEvent>, IAsyncEnumerator<V03.A2AEvent>
    {
        public V03.A2AEvent Current => throw new InvalidOperationException();

        public IAsyncEnumerator<V03.A2AEvent> GetAsyncEnumerator(
            CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(false);

        public ValueTask DisposeAsync() =>
            ValueTask.FromException(new InvalidOperationException("Failed to dispose enumerator."));
    }
}

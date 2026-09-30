using A2A.AspNetCore;
using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.UnitTests.AspNetCore;

public class A2AJsonRpcCustomOperationTests
{
    [Fact]
    public async Task ProcessRequestAsync_CustomOperationUsesRequestScopeUntilResponseExecution()
    {
        var operation = new A2AOperation<CustomRequest, CustomResult>(
            new A2AOperationId("https://example.com/extensions/test#execute"));
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                operation,
                static (context, request, _) =>
                {
                    var prefix = context.Features.GetRequired<CustomFeature>().Prefix;
                    return ValueTask.FromResult(new CustomResult(prefix + request.Value));
                })
            .Build();
        var bindings = new A2AJsonRpcOperationBindingBuilder()
            .Map(
                "test/execute",
                operation,
                CustomJsonContext.Default.CustomRequest,
                CustomJsonContext.Default.CustomResult)
            .Build();
        var scopeCreateCount = 0;
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            var features = new A2AFeatureCollection();
            features.Set(new CustomFeature("handled:"));
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(new TestRequestHandler(), features),
                    () =>
                    {
                        disposeCount++;
                        return ValueTask.CompletedTask;
                    }));
        };
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes(
                """
                {
                  "jsonrpc": "2.0",
                  "id": "request-1",
                  "method": "test/execute",
                  "params": {
                    "value": "request"
                  }
                }
                """));

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            scopeFactory,
            handlers,
            bindings,
            httpContext.Request,
            CancellationToken.None);

        Assert.Equal(1, scopeCreateCount);
        Assert.Equal("A2ARequestScopeResult", result.GetType().Name);
        Assert.Equal(0, disposeCount);

        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;
        await result.ExecuteAsync(httpContext);

        Assert.Equal(1, disposeCount);
        responseBody.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<JsonRpcResponse>(
            responseBody,
            A2AJsonUtilities.DefaultOptions);
        var customResult = response!.Result.Deserialize(
            CustomJsonContext.Default.CustomResult);
        Assert.Equal("handled:request", customResult!.Value);
    }

    internal sealed record CustomRequest(
        [property: JsonPropertyName("value")] string Value);

    internal sealed record CustomResult(
        [property: JsonPropertyName("value")] string Value);

    private sealed record CustomFeature(string Prefix);

    internal sealed class TestRequestHandler(
        Func<GetTaskRequest, Task<AgentTask>>? getTask = null)
        : IA2ARequestHandler
    {
        public Task<SendMessageResponse> SendMessageAsync(
            SendMessageRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(
            SendMessageRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AgentTask> GetTaskAsync(
            GetTaskRequest request,
            CancellationToken cancellationToken = default)
            => getTask?.Invoke(request) ?? throw new NotSupportedException();

        public Task<ListTasksResponse> ListTasksAsync(
            ListTasksRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AgentTask> CancelTaskAsync(
            CancelTaskRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(
            SubscribeToTaskRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(
            TaskPushNotificationConfig config,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(
            GetTaskPushNotificationConfigRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(
            ListTaskPushNotificationConfigsRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteTaskPushNotificationConfigAsync(
            DeleteTaskPushNotificationConfigRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AgentCard> GetExtendedAgentCardAsync(
            GetExtendedAgentCardRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

[JsonSerializable(typeof(A2AJsonRpcCustomOperationTests.CustomRequest))]
[JsonSerializable(typeof(A2AJsonRpcCustomOperationTests.CustomResult))]
internal sealed partial class CustomJsonContext : JsonSerializerContext;

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
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<CustomRequest, CustomResult>(
            new A2AOperationId("https://example.com/extensions/test#execute"));
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                operation,
                static (context, request, _) =>
                {
                    var prefix = context.Features.GetRequired<CustomFeature>().Prefix;
                    return ValueTask.FromResult(new CustomResult(prefix + request.Value));
                })
            .Build(operationCatalog);
        var bindings = new A2AJsonRpcOperationBindingBuilder()
            .Map(
                "test/execute",
                operation,
                CustomJsonContext.Default.CustomRequest,
                CustomJsonContext.Default.CustomResult)
            .Build(operationCatalog);
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

    [Theory]
    [InlineData(
        """
        {
          "jsonrpc": "2.0",
          "id": 42,
          "method": "test/unknown",
          "params": {}
        }
        """,
        (int)A2AErrorCode.MethodNotFound,
        42L)]
    [InlineData(
        """
        {
          "jsonrpc": "2.0",
          "id": "missing-params",
          "method": "test/execute"
        }
        """,
        (int)A2AErrorCode.InvalidParams,
        "missing-params")]
    [InlineData(
        """
        {
          "jsonrpc": "2.0",
          "id": "malformed",
          "method":
        }
        """,
        (int)A2AErrorCode.ParseError,
        null)]
    public async Task ProcessRequestAsync_PreDispatchFailureDoesNotCreateRequestScope(
        string requestJson,
        int expectedErrorCode,
        object? expectedId)
    {
        var operationCatalog = new A2AOperationCatalogBuilder().Build();
        var handlers = new A2AOperationHandlerCatalogBuilder().Build(operationCatalog);
        var bindings = new A2AJsonRpcOperationBindingBuilder().Build(operationCatalog);
        var scopeCreateCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(new TestRequestHandler())));
        };
        var httpContext = CreateHttpContext(requestJson);

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            scopeFactory,
            handlers,
            bindings,
            httpContext.Request,
            CancellationToken.None);
        var response = await ExecuteResponseAsync(httpContext, result);

        Assert.Equal(0, scopeCreateCount);
        Assert.Equal(expectedErrorCode, response.Error?.Code);
        if (expectedId is long numericId)
        {
            Assert.Equal(numericId, response.Id.AsNumber());
        }
        else if (expectedId is string stringId)
        {
            Assert.Equal(stringId, response.Id.AsString());
        }
        else
        {
            Assert.False(response.Id.HasValue);
        }
    }

    [Fact]
    public async Task ProcessRequestAsync_SemanticValidationRunsBeforeRequestScopeCreation()
    {
        var validatorCallCount = 0;
        var handlerCallCount = 0;
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<CustomRequest, CustomResult>(
            new A2AOperationId("https://example.com/extensions/test#validate"),
            request =>
            {
                validatorCallCount++;
                Assert.Equal("invalid", request.Value);
                throw new A2AException(
                    "The custom request is invalid.",
                    A2AErrorCode.InvalidParams);
            });
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                operation,
                (_, _, _) =>
                {
                    handlerCallCount++;
                    return ValueTask.FromResult(new CustomResult("unexpected"));
                })
            .Build(operationCatalog);
        var bindings = new A2AJsonRpcOperationBindingBuilder()
            .Map(
                "test/validate",
                operation,
                CustomJsonContext.Default.CustomRequest,
                CustomJsonContext.Default.CustomResult)
            .Build(operationCatalog);
        var scopeCreateCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(new TestRequestHandler())));
        };
        var httpContext = CreateHttpContext(
            """
            {
              "jsonrpc": "2.0",
              "id": "validation",
              "method": "test/validate",
              "params": {
                "value": "invalid"
              }
            }
            """);

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            scopeFactory,
            handlers,
            bindings,
            httpContext.Request,
            CancellationToken.None);
        var response = await ExecuteResponseAsync(httpContext, result);

        Assert.Equal(1, validatorCallCount);
        Assert.Equal(0, scopeCreateCount);
        Assert.Equal(0, handlerCallCount);
        Assert.Equal((int)A2AErrorCode.InvalidParams, response.Error?.Code);
        Assert.Equal("validation", response.Id.AsString());
    }

    [Fact]
    public async Task ProcessRequestAsync_CustomStreamingOperationDisposesScopeAfterEnumeration()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineStreaming<CustomRequest, CustomStreamEvent>(
            new A2AOperationId("https://example.com/extensions/test#stream"));
        var operationCatalog = operationBuilder.Build();
        var streamCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .MapStreaming(
                operation,
                (_, request, cancellationToken) =>
                    YieldStreamEventAsync(
                        new CustomStreamEvent(request.Value),
                        streamCompleted,
                        cancellationToken))
            .Build(operationCatalog);
        var bindings = new A2AJsonRpcOperationBindingBuilder()
            .MapStreaming(
                "test/stream",
                operation,
                CustomJsonContext.Default.CustomRequest,
                CustomJsonContext.Default.CustomStreamEvent)
            .Build(operationCatalog);
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(new TestRequestHandler()),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var httpContext = CreateHttpContext(
            """
            {
              "jsonrpc": "2.0",
              "id": 73,
              "method": "test/stream",
              "params": {
                "value": "event-1"
              }
            }
            """);

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            scopeFactory,
            handlers,
            bindings,
            httpContext.Request,
            CancellationToken.None);

        Assert.Equal(0, disposeCount);

        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;
        await result.ExecuteAsync(httpContext);

        Assert.True(streamCompleted.Task.IsCompletedSuccessfully);
        Assert.Equal(1, disposeCount);
        Assert.Equal("text/event-stream", httpContext.Response.ContentType);
        var responseText = Encoding.UTF8.GetString(responseBody.ToArray());
        Assert.Contains("\"id\":73", responseText, StringComparison.Ordinal);
        Assert.Contains("\"value\":\"event-1\"", responseText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessRequestAsync_DeclaredOperationErrorUsesConfiguredCodeAndDetailsMetadata()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<CustomRequest, CustomResult>(
            new A2AOperationId("https://example.com/extensions/test#declared-error"));
        var declaredError = operationBuilder.DeclareError<
            CustomRequest,
            CustomResult,
            CustomErrorDetails>(
                operation,
                "https://example.com/errors/not-found");
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map<CustomRequest, CustomResult>(
                operation,
                (_, _, _) => throw new A2AOperationException<CustomErrorDetails>(
                    declaredError,
                    "The authorization request was not found.",
                    new CustomErrorDetails("authorization-1")))
            .Build(operationCatalog);
        var bindings = new A2AJsonRpcOperationBindingBuilder()
            .Map(
                "test/declared-error",
                operation,
                CustomJsonContext.Default.CustomRequest,
                CustomJsonContext.Default.CustomResult)
            .MapError(
                operation,
                declaredError,
                -32081,
                CustomJsonContext.Default.CustomErrorDetails)
            .Build(operationCatalog);
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(new TestRequestHandler()),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var httpContext = CreateHttpContext(
            """
            {
              "jsonrpc": "2.0",
              "id": "declared-error",
              "method": "test/declared-error",
              "params": {
                "value": "request"
              }
            }
            """);

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            scopeFactory,
            handlers,
            bindings,
            httpContext.Request,
            CancellationToken.None);

        Assert.Equal(0, disposeCount);
        var response = await ExecuteResponseAsync(httpContext, result);

        Assert.Equal(1, disposeCount);
        Assert.Equal("declared-error", response.Id.AsString());
        Assert.Equal(-32081, response.Error?.Code);
        Assert.Equal(
            "The authorization request was not found.",
            response.Error?.Message);
        Assert.Equal(
            "authorization-1",
            response.Error?.Data?.GetProperty("authorizationRequestId").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessRequestAsync_UnmappedOrUnserializableOperationErrorReturnsGenericInternalError(
        bool mapErrorWithFailingDetails)
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<CustomRequest, CustomResult>(
            new A2AOperationId("https://example.com/extensions/test#internal-error"));
        var declaredError = operationBuilder.DeclareError<
            CustomRequest,
            CustomResult,
            ThrowingErrorDetails>(
                operation,
                "https://example.com/errors/internal");
        var operationCatalog = operationBuilder.Build();
        const string secretMessage = "secret handler failure";
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map<CustomRequest, CustomResult>(
                operation,
                (_, _, _) => throw new A2AOperationException<ThrowingErrorDetails>(
                    declaredError,
                    secretMessage,
                    new ThrowingErrorDetails()))
            .Build(operationCatalog);
        var bindingBuilder = new A2AJsonRpcOperationBindingBuilder()
            .Map(
                "test/internal-error",
                operation,
                CustomJsonContext.Default.CustomRequest,
                CustomJsonContext.Default.CustomResult);
        if (mapErrorWithFailingDetails)
        {
            bindingBuilder.MapError(
                operation,
                declaredError,
                -32082,
                CustomJsonContext.Default.ThrowingErrorDetails);
        }

        var bindings = bindingBuilder.Build(operationCatalog);
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(new TestRequestHandler())));
        var httpContext = CreateHttpContext(
            """
            {
              "jsonrpc": "2.0",
              "id": "internal-error",
              "method": "test/internal-error",
              "params": {
                "value": "request"
              }
            }
            """);

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            scopeFactory,
            handlers,
            bindings,
            httpContext.Request,
            CancellationToken.None);
        var response = await ExecuteResponseAsync(httpContext, result);

        Assert.Equal((int)A2AErrorCode.InternalError, response.Error?.Code);
        Assert.Equal("An internal error occurred.", response.Error?.Message);
        Assert.DoesNotContain(
            secretMessage,
            JsonSerializer.Serialize(response, A2AJsonUtilities.DefaultOptions),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WhenErrorMappingUsesAnotherOperationsError_Throws()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<CustomRequest, CustomResult>(
            new A2AOperationId("https://example.com/extensions/test#operation"));
        var otherOperation = operationBuilder.DefineUnary<CustomRequest, CustomResult>(
            new A2AOperationId("https://example.com/extensions/test#other-operation"));
        var otherError = operationBuilder.DeclareError<
            CustomRequest,
            CustomResult,
            CustomErrorDetails>(
                otherOperation,
                "https://example.com/errors/other");
        var operationCatalog = operationBuilder.Build();
        var bindingBuilder = new A2AJsonRpcOperationBindingBuilder()
            .Map(
                "test/operation",
                operation,
                CustomJsonContext.Default.CustomRequest,
                CustomJsonContext.Default.CustomResult)
            .MapError(
                operation,
                otherError,
                -32083,
                CustomJsonContext.Default.CustomErrorDetails);

        var exception = Assert.Throws<InvalidOperationException>(
            () => bindingBuilder.Build(operationCatalog));

        Assert.Contains(
            otherError.ErrorId,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MapError_WhenMappingAlreadyExists_Throws()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<CustomRequest, CustomResult>(
            new A2AOperationId("https://example.com/extensions/test#duplicate-error"));
        var declaredError = operationBuilder.DeclareError<
            CustomRequest,
            CustomResult,
            CustomErrorDetails>(
                operation,
                "https://example.com/errors/duplicate");
        var bindingBuilder = new A2AJsonRpcOperationBindingBuilder()
            .Map(
                "test/duplicate-error",
                operation,
                CustomJsonContext.Default.CustomRequest,
                CustomJsonContext.Default.CustomResult)
            .MapError(
                operation,
                declaredError,
                -32084,
                CustomJsonContext.Default.CustomErrorDetails);

        var exception = Assert.Throws<InvalidOperationException>(
            () => bindingBuilder.MapError(
                operation,
                declaredError,
                -32084,
                CustomJsonContext.Default.CustomErrorDetails));

        Assert.Contains(
            declaredError.ErrorId,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapError_AppliesToBindingAddedAfterTheMapping()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<CustomRequest, CustomResult>(
            new A2AOperationId("https://example.com/extensions/test#multi-binding-error"));
        var declaredError = operationBuilder.DeclareError<
            CustomRequest,
            CustomResult,
            CustomErrorDetails>(
                operation,
                "https://example.com/errors/multi-binding");
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map<CustomRequest, CustomResult>(
                operation,
                (_, _, _) => throw new A2AOperationException<CustomErrorDetails>(
                    declaredError,
                    "The second binding failed.",
                    new CustomErrorDetails("multi-binding")))
            .Build(operationCatalog);
        var bindings = new A2AJsonRpcOperationBindingBuilder()
            .Map(
                "test/first-binding",
                operation,
                CustomJsonContext.Default.CustomRequest,
                CustomJsonContext.Default.CustomResult)
            .MapError(
                operation,
                declaredError,
                -32086,
                CustomJsonContext.Default.CustomErrorDetails)
            .Map(
                "test/second-binding",
                operation,
                CustomJsonContext.Default.CustomRequest,
                CustomJsonContext.Default.CustomResult)
            .Build(operationCatalog);
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(new TestRequestHandler())));
        var httpContext = CreateHttpContext(
            """
            {
              "jsonrpc": "2.0",
              "id": "second-binding",
              "method": "test/second-binding",
              "params": {
                "value": "request"
              }
            }
            """);

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            scopeFactory,
            handlers,
            bindings,
            httpContext.Request,
            CancellationToken.None);
        var response = await ExecuteResponseAsync(httpContext, result);

        Assert.Equal(-32086, response.Error?.Code);
        Assert.Equal(
            "multi-binding",
            response.Error?.Data?.GetProperty("authorizationRequestId").GetString());
    }

    [Fact]
    public async Task ProcessRequestAsync_StreamingDeclaredErrorBeforeFirstEventUsesConfiguredMapping()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineStreaming<CustomRequest, CustomStreamEvent>(
            new A2AOperationId("https://example.com/extensions/test#stream-error"));
        var declaredError = operationBuilder.DeclareError<
            CustomRequest,
            CustomStreamEvent,
            CustomErrorDetails>(
                operation,
                "https://example.com/errors/stream");
        var operationCatalog = operationBuilder.Build();
        var operationException = new A2AOperationException<CustomErrorDetails>(
            declaredError,
            "The stream could not start.",
            new CustomErrorDetails("stream-authorization"));
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .MapStreaming(
                operation,
                (_, _, _) =>
                    ThrowBeforeFirstEventAsync<CustomStreamEvent>(operationException))
            .Build(operationCatalog);
        var bindings = new A2AJsonRpcOperationBindingBuilder()
            .MapStreaming(
                "test/stream-error",
                operation,
                CustomJsonContext.Default.CustomRequest,
                CustomJsonContext.Default.CustomStreamEvent)
            .MapError(
                operation,
                declaredError,
                -32085,
                CustomJsonContext.Default.CustomErrorDetails)
            .Build(operationCatalog);
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(new TestRequestHandler()),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var httpContext = CreateHttpContext(
            """
            {
              "jsonrpc": "2.0",
              "id": "stream-error",
              "method": "test/stream-error",
              "params": {
                "value": "request"
              }
            }
            """);

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            scopeFactory,
            handlers,
            bindings,
            httpContext.Request,
            CancellationToken.None);
        var response = await ExecuteResponseAsync(httpContext, result);

        Assert.Equal(1, disposeCount);
        Assert.Equal("application/json", httpContext.Response.ContentType);
        Assert.Equal(-32085, response.Error?.Code);
        Assert.Equal(
            "stream-authorization",
            response.Error?.Data?.GetProperty("authorizationRequestId").GetString());
    }

    private static DefaultHttpContext CreateHttpContext(string requestJson)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes(requestJson));
        return httpContext;
    }

    private static async Task<JsonRpcResponse> ExecuteResponseAsync(
        DefaultHttpContext httpContext,
        IResult result)
    {
        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;
        await result.ExecuteAsync(httpContext);
        responseBody.Position = 0;
        return (await JsonSerializer.DeserializeAsync<JsonRpcResponse>(
            responseBody,
            A2AJsonUtilities.DefaultOptions))!;
    }

    private static async IAsyncEnumerable<CustomStreamEvent> YieldStreamEventAsync(
        CustomStreamEvent streamEvent,
        TaskCompletionSource streamCompleted,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return streamEvent;
        streamCompleted.SetResult();
        await Task.Yield();
    }

    private static async IAsyncEnumerable<T> ThrowBeforeFirstEventAsync<T>(
        Exception? failure)
    {
        await Task.Yield();
        if (failure is not null)
        {
            throw failure;
        }

        yield break;
    }

    internal sealed record CustomRequest(
        [property: JsonPropertyName("value")] string Value);

    internal sealed record CustomResult(
        [property: JsonPropertyName("value")] string Value);

    internal sealed record CustomStreamEvent(
        [property: JsonPropertyName("value")] string Value);

    internal sealed record CustomErrorDetails(
        [property: JsonPropertyName("authorizationRequestId")]
        string AuthorizationRequestId);

    internal sealed class ThrowingErrorDetails
    {
        private readonly object _instanceMarker = new();

        [JsonPropertyName("secret")]
        public string Secret
        {
            get
            {
                _ = _instanceMarker;
                throw new InvalidOperationException(
                    "secret detail serialization failure");
            }
        }
    }

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
[JsonSerializable(typeof(A2AJsonRpcCustomOperationTests.CustomStreamEvent))]
[JsonSerializable(typeof(A2AJsonRpcCustomOperationTests.CustomErrorDetails))]
[JsonSerializable(typeof(A2AJsonRpcCustomOperationTests.ThrowingErrorDetails))]
internal sealed partial class CustomJsonContext : JsonSerializerContext;

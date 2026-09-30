using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.UnitTests.Client;

public partial class A2AClientCustomOperationTests
{
    [Fact]
    public async Task InvokeAsync_JsonRpcUsesRegisteredMethodAndSerializationMetadata()
    {
        var operation = new A2AOperation<CustomRequest, CustomResult>(
            new A2AOperationId("test.execute"));
        var bindings = new A2AClientOperationBindingBuilder()
            .MapJsonRpc(
                operation,
                "test/execute",
                CustomClientJsonContext.Default.CustomRequest,
                CustomClientJsonContext.Default.CustomResult,
                static (message, request) =>
                    message.Headers.TryAddWithoutValidation(
                        "X-Custom-Value",
                        request.Value))
            .Build();
        string? capturedBody = null;
        string[]? capturedCustomHeader = null;
        var httpClient = new HttpClient(new RecordingHandler(
            request =>
            {
                capturedBody = request.Content!.ReadAsStringAsync()
                    .GetAwaiter()
                    .GetResult();
                capturedCustomHeader = request.Headers
                    .GetValues("X-Custom-Value")
                    .ToArray();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "jsonrpc": "2.0",
                          "id": "response-1",
                          "result": { "value": "json-rpc-result" }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json"),
                };
            }));
        var client = new A2AClient(
            new Uri("http://localhost/a2a"),
            bindings,
            httpClient);

        var result = await client.InvokeAsync(
            operation,
            new CustomRequest("request-value"));

        Assert.Equal("json-rpc-result", result.Value);
        using var requestJson = JsonDocument.Parse(capturedBody!);
        Assert.Equal(
            "test/execute",
            requestJson.RootElement.GetProperty("method").GetString());
        Assert.Equal(
            "request-value",
            requestJson.RootElement
                .GetProperty("params")
                .GetProperty("value")
                .GetString());
        Assert.Equal(
            "request-value",
            capturedCustomHeader!.Single());
    }

    [Fact]
    public async Task InvokeAsync_HttpJsonUsesRegisteredRequestMapperAndSerializationMetadata()
    {
        var operation = new A2AOperation<CustomRequest, CustomResult>(
            new A2AOperationId("test.execute"));
        var bindings = new A2AClientOperationBindingBuilder()
            .MapHttp(
                operation,
                static (endpoint, request, _) =>
                {
                    var message = new HttpRequestMessage(
                        HttpMethod.Post,
                        new Uri(endpoint, $"/tasks/{request.Value}:execute"))
                    {
                        Content = new StringContent(
                            """{"token":"opaque-token"}""",
                            Encoding.UTF8,
                            "application/json"),
                    };
                    message.Headers.TryAddWithoutValidation(
                        "X-Extension",
                        "enabled");
                    return ValueTask.FromResult(message);
                },
                CustomClientJsonContext.Default.CustomResult)
            .Build();
        HttpRequestMessage? capturedRequest = null;
        var httpClient = new HttpClient(new RecordingHandler(
            request =>
            {
                capturedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"value":"http-result"}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }));
        var client = new A2AHttpJsonClient(
            new Uri("http://localhost/a2a"),
            bindings,
            httpClient);

        var result = await client.InvokeAsync(
            operation,
            new CustomRequest("task-1"));

        Assert.Equal("http-result", result.Value);
        Assert.NotNull(capturedRequest);
        Assert.Equal(
            "http://localhost/tasks/task-1:execute",
            capturedRequest.RequestUri!.ToString());
        Assert.Equal(
            "enabled",
            capturedRequest.Headers.GetValues("X-Extension").Single());
        Assert.Equal(
            "1.0",
            capturedRequest.Headers.GetValues("A2A-Version").Single());
    }

    [Fact]
    public async Task InvokeStreamingAsync_JsonRpcUsesRegisteredBindingAndEventMetadata()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineStreaming<
            CustomRequest,
            CustomStreamEvent>(
                new A2AOperationId("test.stream"));
        var catalog = operationBuilder.Build();
        var bindings = new A2AClientOperationBindingBuilder()
            .MapJsonRpcStreaming(
                operation,
                "test/stream",
                CustomClientJsonContext.Default.CustomRequest,
                CustomClientJsonContext.Default.CustomStreamEvent)
            .Build(catalog);
        string? capturedBody = null;
        var httpClient = new HttpClient(new RecordingHandler(
            request =>
            {
                capturedBody = request.Content!.ReadAsStringAsync()
                    .GetAwaiter()
                    .GetResult();
                return CreateSseResponse(
                    """
                    {
                      "jsonrpc": "2.0",
                      "id": "response-1",
                      "result": { "sequence": 7 }
                    }
                    """);
            }));
        var client = new A2AClient(
            new Uri("http://localhost/a2a"),
            bindings,
            httpClient);

        var events = await ToListAsync(
            client.InvokeStreamingAsync(
                operation,
                new CustomRequest("request-value")));

        Assert.Equal(7, Assert.Single(events).Sequence);
        using var requestJson = JsonDocument.Parse(capturedBody!);
        Assert.Equal(
            "test/stream",
            requestJson.RootElement.GetProperty("method").GetString());
        Assert.Equal(
            "request-value",
            requestJson.RootElement
                .GetProperty("params")
                .GetProperty("value")
                .GetString());
    }

    [Fact]
    public async Task InvokeStreamingAsync_HttpJsonUsesRegisteredRequestMapperAndEventMetadata()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineStreaming<
            CustomRequest,
            CustomStreamEvent>(
                new A2AOperationId("test.stream"));
        var catalog = operationBuilder.Build();
        var bindings = new A2AClientOperationBindingBuilder()
            .MapHttpStreaming(
                operation,
                static (endpoint, request, _) =>
                    ValueTask.FromResult(
                        new HttpRequestMessage(
                            HttpMethod.Post,
                            new Uri(
                                endpoint,
                                $"/tasks/{request.Value}:stream"))),
                CustomClientJsonContext.Default.CustomStreamEvent)
            .Build(catalog);
        HttpRequestMessage? capturedRequest = null;
        var httpClient = new HttpClient(new RecordingHandler(
            request =>
            {
                capturedRequest = request;
                return CreateSseResponse("""{"sequence":9}""");
            }));
        var client = new A2AHttpJsonClient(
            new Uri("http://localhost/a2a"),
            bindings,
            httpClient);

        var events = await ToListAsync(
            client.InvokeStreamingAsync(
                operation,
                new CustomRequest("task-1")));

        Assert.Equal(9, Assert.Single(events).Sequence);
        Assert.NotNull(capturedRequest);
        Assert.Equal(
            "http://localhost/tasks/task-1:stream",
            capturedRequest.RequestUri!.ToString());
        Assert.Equal(
            "text/event-stream",
            capturedRequest.Headers.Accept.Single().MediaType);
    }

    [Fact]
    public async Task InvokeStreamingAsync_IsLazyPropagatesTokenAndDisposesResponse()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineStreaming<
            CustomRequest,
            CustomStreamEvent>(
                new A2AOperationId("test.stream"));
        var catalog = operationBuilder.Build();
        var bindings = new A2AClientOperationBindingBuilder()
            .MapHttpStreaming(
                operation,
                static (endpoint, _, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(
                        new HttpRequestMessage(HttpMethod.Post, endpoint));
                },
                CustomClientJsonContext.Default.CustomStreamEvent)
            .Build(catalog);
        var trackingStream = new TrackingStream(
            Encoding.UTF8.GetBytes(
                "data: {\"sequence\":11}\n\n"));
        var trackingContent = new TrackingContent(trackingStream);
        var trackingResponse = new TrackingResponse(HttpStatusCode.OK)
        {
            Content = trackingContent,
        };
        var handler = new TrackingHandler(trackingResponse);
        var client = new A2AHttpJsonClient(
            new Uri("http://localhost/a2a"),
            bindings,
            new HttpClient(handler));
        using var cancellationSource = new CancellationTokenSource();
        var events = client.InvokeStreamingAsync(
            operation,
            new CustomRequest("request-value"),
            cancellationSource.Token);

        Assert.False(handler.WasCalled);
        var enumerator = events.GetAsyncEnumerator(
            cancellationSource.Token);
        try
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(11, enumerator.Current.Sequence);
            Assert.True(handler.CancellationToken.CanBeCanceled);
            Assert.True(trackingContent.CancellationToken.CanBeCanceled);
            Assert.True(trackingStream.CancellationToken.CanBeCanceled);
            Assert.False(trackingResponse.IsDisposed);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.True(trackingResponse.IsDisposed);
        Assert.True(trackingStream.IsDisposed);
    }

    [Fact]
    public async Task InvokeStreamingAsync_CancellationCancelsRequestSend()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineStreaming<
            CustomRequest,
            CustomStreamEvent>(
                new A2AOperationId("test.stream"));
        var catalog = operationBuilder.Build();
        var bindings = new A2AClientOperationBindingBuilder()
            .MapJsonRpcStreaming(
                operation,
                "test/stream",
                CustomClientJsonContext.Default.CustomRequest,
                CustomClientJsonContext.Default.CustomStreamEvent)
            .Build(catalog);
        var handler = new CancellationHandler();
        var client = new A2AClient(
            new Uri("http://localhost/a2a"),
            bindings,
            new HttpClient(handler));
        using var cancellationSource = new CancellationTokenSource();
        var enumerator = client.InvokeStreamingAsync(
                operation,
                new CustomRequest("request-value"))
            .GetAsyncEnumerator(cancellationSource.Token);

        var moveNextTask = enumerator.MoveNextAsync().AsTask();
        await handler.RequestStarted.Task;
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => moveNextTask);
        Assert.True(handler.CancellationToken.IsCancellationRequested);
        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task InvokeStreamingAsync_MissingJsonRpcBindingFailsBeforeRequest()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineStreaming<
            CustomRequest,
            CustomStreamEvent>(
                new A2AOperationId("test.stream"));
        var catalog = operationBuilder.Build();
        var bindings = new A2AClientOperationBindingBuilder()
            .MapHttpStreaming(
                operation,
                static (endpoint, _, _) => ValueTask.FromResult(
                    new HttpRequestMessage(HttpMethod.Post, endpoint)),
                CustomClientJsonContext.Default.CustomStreamEvent)
            .Build(catalog);
        var requestCount = 0;
        var client = new A2AClient(
            new Uri("http://localhost/a2a"),
            bindings,
            new HttpClient(new RecordingHandler(_ =>
            {
                requestCount++;
                throw new InvalidOperationException("Request should not be sent.");
            })));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ToListAsync(
                client.InvokeStreamingAsync(
                    operation,
                    new CustomRequest("request-value"))));

        Assert.Contains("No JSON-RPC client binding", exception.Message);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task InvokeAsync_IncompatibleOperationHandleFailsBeforeRequest()
    {
        var operationId = new A2AOperationId("test.execute");
        var firstBuilder = new A2AOperationCatalogBuilder();
        var configuredOperation = firstBuilder.DefineUnary<
            CustomRequest,
            CustomResult>(operationId);
        var configuredCatalog = firstBuilder.Build();
        var bindings = new A2AClientOperationBindingBuilder()
            .MapJsonRpc(
                configuredOperation,
                "test/execute",
                CustomClientJsonContext.Default.CustomRequest,
                CustomClientJsonContext.Default.CustomResult)
            .Build(configuredCatalog);
        var secondBuilder = new A2AOperationCatalogBuilder();
        var incompatibleOperation = secondBuilder.DefineUnary<
            CustomRequest,
            CustomResult>(operationId);
        var requestCount = 0;
        var client = new A2AClient(
            new Uri("http://localhost/a2a"),
            bindings,
            new HttpClient(new RecordingHandler(_ =>
            {
                requestCount++;
                throw new InvalidOperationException("Request should not be sent.");
            })));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.InvokeAsync(
                incompatibleOperation,
                new CustomRequest("request-value")));

        Assert.Contains(
            "does not belong to the configured client bindings",
            exception.Message);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task InvokeAsync_ValidatorRunsOnceBeforeHttpRequestMapper()
    {
        var validationCount = 0;
        var mapperCalled = false;
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<
            CustomRequest,
            CustomResult>(
                new A2AOperationId("test.validate"),
                request =>
                {
                    validationCount++;
                    Assert.Equal("invalid", request.Value);
                    throw new A2AException(
                        "Rejected by validator.",
                        A2AErrorCode.InvalidParams);
                });
        var catalog = operationBuilder.Build();
        var bindings = new A2AClientOperationBindingBuilder()
            .MapHttp(
                operation,
                (_, _, _) =>
                {
                    mapperCalled = true;
                    throw new InvalidOperationException(
                        "Mapper should not be called.");
                },
                CustomClientJsonContext.Default.CustomResult)
            .Build(catalog);
        var requestCount = 0;
        var client = new A2AHttpJsonClient(
            new Uri("http://localhost/a2a"),
            bindings,
            new HttpClient(new RecordingHandler(_ =>
            {
                requestCount++;
                throw new InvalidOperationException("Request should not be sent.");
            })));

        var exception = await Assert.ThrowsAsync<A2AException>(
            () => client.InvokeAsync(
                operation,
                new CustomRequest("invalid")));

        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Equal(1, validationCount);
        Assert.False(mapperCalled);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task InvokeStreamingAsync_JsonRpcDeclaredErrorBecomesTypedException()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineStreaming<
            CustomRequest,
            CustomStreamEvent>(
                new A2AOperationId("test.stream"));
        var error = operationBuilder.DeclareError<
            CustomRequest,
            CustomStreamEvent,
            CustomErrorDetails>(
                operation,
                "https://example.com/errors/not-ready");
        var catalog = operationBuilder.Build();
        var bindings = new A2AClientOperationBindingBuilder()
            .MapJsonRpcStreaming(
                operation,
                "test/stream",
                CustomClientJsonContext.Default.CustomRequest,
                CustomClientJsonContext.Default.CustomStreamEvent)
            .MapJsonRpcError(
                operation,
                error,
                -32081,
                CustomClientJsonContext.Default.CustomErrorDetails)
            .Build(catalog);
        var httpClient = new HttpClient(new RecordingHandler(
            _ => CreateSseResponse(
                """
                {
                  "jsonrpc": "2.0",
                  "id": "response-1",
                  "error": {
                    "code": -32081,
                    "message": "Authorization is not ready.",
                    "data": { "requestId": "request-1" }
                  }
                }
                """)));
        var client = new A2AClient(
            new Uri("http://localhost/a2a"),
            bindings,
            httpClient);

        var exception = await Assert.ThrowsAsync<
            A2AOperationException<CustomErrorDetails>>(
                () => ToListAsync(
                    client.InvokeStreamingAsync(
                        operation,
                        new CustomRequest("request-value"))));

        Assert.Same(error, exception.Error);
        Assert.Equal("request-1", exception.Details.RequestId);
        Assert.Equal(
            "Authorization is not ready.",
            exception.Message);
    }

    [Fact]
    public async Task InvokeAsync_HttpDeclaredErrorBecomesTypedException()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<
            CustomRequest,
            CustomResult>(
                new A2AOperationId("test.execute"));
        var error = operationBuilder.DeclareError<
            CustomRequest,
            CustomResult,
            CustomErrorDetails>(
                operation,
                "https://example.com/errors/not-ready");
        var catalog = operationBuilder.Build();
        var bindings = new A2AClientOperationBindingBuilder()
            .MapHttp(
                operation,
                static (endpoint, _, _) =>
                    ValueTask.FromResult(
                        new HttpRequestMessage(HttpMethod.Post, endpoint)),
                CustomClientJsonContext.Default.CustomResult)
            .MapHttpError(
                operation,
                error,
                (int)HttpStatusCode.Conflict,
                CustomClientJsonContext.Default.CustomErrorDetails)
            .Build(catalog);
        var httpClient = new HttpClient(new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(
                    """
                    {
                      "error": {
                        "code": 409,
                        "status": "ABORTED",
                        "message": "Authorization is not ready.",
                        "details": [
                          {
                            "@type": "https://example.com/errors/not-ready",
                            "requestId": "request-2"
                          }
                        ]
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            }));
        var client = new A2AHttpJsonClient(
            new Uri("http://localhost/a2a"),
            bindings,
            httpClient);

        var exception = await Assert.ThrowsAsync<
            A2AOperationException<CustomErrorDetails>>(
                () => client.InvokeAsync(
                    operation,
                    new CustomRequest("request-value")));

        Assert.Same(error, exception.Error);
        Assert.Equal("request-2", exception.Details.RequestId);
        Assert.Equal(
            "Authorization is not ready.",
            exception.Message);
    }

    [Fact]
    public async Task InvokeAsync_MalformedDeclaredErrorRetainsGenericJsonRpcBehavior()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<
            CustomRequest,
            CustomResult>(
                new A2AOperationId("test.execute"));
        var error = operationBuilder.DeclareError<
            CustomRequest,
            CustomResult,
            CustomErrorDetails>(
                operation,
                "https://example.com/errors/not-ready");
        var catalog = operationBuilder.Build();
        var bindings = new A2AClientOperationBindingBuilder()
            .MapJsonRpc(
                operation,
                "test/execute",
                CustomClientJsonContext.Default.CustomRequest,
                CustomClientJsonContext.Default.CustomResult)
            .MapJsonRpcError(
                operation,
                error,
                -32081,
                CustomClientJsonContext.Default.CustomErrorDetails)
            .Build(catalog);
        var httpClient = new HttpClient(new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "jsonrpc": "2.0",
                      "id": "response-1",
                      "error": {
                        "code": -32081,
                        "message": "Authorization is not ready.",
                        "data": { "requestId": { "unexpected": true } }
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            }));
        var client = new A2AClient(
            new Uri("http://localhost/a2a"),
            bindings,
            httpClient);

        var exception = await Assert.ThrowsAsync<A2AException>(
            () => client.InvokeAsync(
                operation,
                new CustomRequest("request-value")));

        Assert.Equal((A2AErrorCode)(-32081), exception.ErrorCode);
        Assert.IsNotType<A2AOperationException<CustomErrorDetails>>(exception);
        Assert.DoesNotContain("unexpected", exception.Message);
    }

    [Fact]
    public async Task ClientFactory_CreateWithBindingsSupportsGenericInvocation()
    {
        var operation = new A2AOperation<CustomRequest, CustomResult>(
            new A2AOperationId("test.execute"));
        var bindings = new A2AClientOperationBindingBuilder()
            .MapJsonRpc(
                operation,
                "test/execute",
                CustomClientJsonContext.Default.CustomRequest,
                CustomClientJsonContext.Default.CustomResult)
            .Build();
        var httpClient = new HttpClient(new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "jsonrpc": "2.0",
                      "id": "response-1",
                      "result": { "value": "factory-result" }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            }));
        var agentCard = new AgentCard
        {
            Name = "test",
            Description = "test",
            Version = "1.0",
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    ProtocolBinding = ProtocolBindingNames.JsonRpc,
                    Url = "http://localhost/a2a",
                },
            ],
        };

        var client = A2AClientFactory.Create(
            agentCard,
            bindings,
            httpClient);
        var result = await client.InvokeAsync(
            operation,
            new CustomRequest("request-value"));

        Assert.Equal("factory-result", result.Value);
    }

    [Fact]
    public void ClientFactory_CustomProtocolReceivesOperationBindings()
    {
        var protocolBinding = $"CUSTOM-{Guid.NewGuid():N}";
        var bindings = new A2AClientOperationBindingBuilder().Build();
        A2AClientOperationBindings? receivedBindings = null;
        A2AClientFactory.RegisterWithOperationBindings(
            protocolBinding,
            (_, _, operationBindings) =>
            {
                receivedBindings = operationBindings;
                throw new ExpectedFactoryInvocationException();
            });
        var agentCard = new AgentCard
        {
            Name = "test",
            Description = "test",
            Version = "1.0",
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    ProtocolBinding = protocolBinding,
                    Url = "http://localhost/a2a",
                },
            ],
        };
        var options = new A2AClientOptions
        {
            PreferredBindings = [protocolBinding],
        };

        Assert.Throws<ExpectedFactoryInvocationException>(
            () => A2AClientFactory.Create(agentCard, bindings, options: options));
        Assert.Same(bindings, receivedBindings);
    }

    internal sealed record CustomRequest(
        [property: JsonPropertyName("value")] string Value);

    internal sealed record CustomResult(
        [property: JsonPropertyName("value")] string Value);

    internal sealed record CustomStreamEvent(
        [property: JsonPropertyName("sequence")] int Sequence);

    internal sealed record CustomErrorDetails(
        [property: JsonPropertyName("requestId")] string RequestId);

    [JsonSerializable(typeof(CustomRequest))]
    [JsonSerializable(typeof(CustomResult))]
    [JsonSerializable(typeof(CustomStreamEvent))]
    [JsonSerializable(typeof(CustomErrorDetails))]
    internal sealed partial class CustomClientJsonContext : JsonSerializerContext;

    private static HttpResponseMessage CreateSseResponse(string data) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"data: {data.Trim().ReplaceLineEndings("\ndata: ")}\n\n",
                Encoding.UTF8,
                "text/event-stream"),
        };

    private static async Task<List<T>> ToListAsync<T>(
        IAsyncEnumerable<T> items,
        CancellationToken cancellationToken = default)
    {
        var results = new List<T>();
        await foreach (var item in items.WithCancellation(cancellationToken))
        {
            results.Add(item);
        }

        return results;
    }

    private sealed class ExpectedFactoryInvocationException : Exception;

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }

    private sealed class TrackingHandler(HttpResponseMessage response)
        : HttpMessageHandler
    {
        internal bool WasCalled { get; private set; }

        internal CancellationToken CancellationToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            CancellationToken = cancellationToken;
            return Task.FromResult(response);
        }
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        internal TaskCompletionSource RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal CancellationToken CancellationToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            RequestStarted.TrySetResult();
            var never = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await never.Task.WaitAsync(cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class TrackingContent(Stream stream) : HttpContent
    {
        internal CancellationToken CancellationToken { get; private set; }

        protected override Task SerializeToStreamAsync(
            Stream target,
            TransportContext? context) =>
            throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync(
            CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            return Task.FromResult(stream);
        }
    }

    private sealed class TrackingStream(byte[] content) : MemoryStream(content)
    {
        internal CancellationToken CancellationToken { get; private set; }

        internal bool IsDisposed { get; private set; }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            CancellationToken = cancellationToken;
            return base.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            return base.ReadAsync(
                buffer,
                offset,
                count,
                cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingResponse(HttpStatusCode statusCode)
        : HttpResponseMessage(statusCode)
    {
        internal bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}

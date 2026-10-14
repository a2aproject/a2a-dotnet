namespace A2A.Grpc.UnitTests;

using global::Grpc.Core;
using System.Collections.Frozen;

public sealed class GrpcExtensionErrorMetadataTests
{
    private const string ExtensionUri = "https://example.org/extensions/debate/v1";
    private const string OtherUri = "https://example.org/extensions/debate/v2";

    [Theory]
    [InlineData("initial", false)]
    [InlineData("initial", true)]
    [InlineData("trailers", false)]
    [InlineData("trailers", true)]
    [InlineData("conflict", false)]
    [InlineData("conflict", true)]
    [InlineData("malformed", false)]
    [InlineData("malformed", true)]
    [InlineData("headers-fail", false)]
    [InlineData("headers-fail", true)]
    [InlineData("empty", false)]
    [InlineData("empty", true)]
    public async Task RemoteError_PreservesCodeAndUsesTheInitialSnapshotOrTrailerFallback(string scenario, bool streaming)
    {
        var headers = new Metadata();
        var rpcError = GrpcErrorMapping.ToRpcException(new A2AException("missing task", A2AErrorCode.TaskNotFound));
        if (scenario is "initial" or "conflict") headers.Add("a2a-extensions", $" {ExtensionUri}, {ExtensionUri}");
        if (scenario == "malformed") headers.Add("a2a-extensions", "not an identifier");
        if (scenario is "trailers" or "headers-fail") rpcError.Trailers.Add("a2a-extensions", ExtensionUri);
        if (scenario is "conflict" or "malformed") rpcError.Trailers.Add("a2a-extensions", OtherUri);
        var invoker = new FailingCallInvoker(headers, rpcError, failHeaders: scenario == "headers-fail");
        using var client = new A2AGrpcClient(new Protos.A2AService.A2AServiceClient(invoker));

        var error = streaming
            ? await Assert.ThrowsAsync<A2AException>(async () =>
            {
                await foreach (var _ in client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "missing" })) { }
            })
            : await Assert.ThrowsAsync<A2AException>(() => client.GetTaskAsync(new GetTaskRequest { Id = "missing" }));

        Assert.Equal(A2AErrorCode.TaskNotFound, error.ErrorCode);
        Assert.Same(rpcError, error.InnerException);
        if (scenario is "empty" or "malformed") Assert.Empty(error.ActivatedExtensions);
        else Assert.Equal(ExtensionUri, Assert.Single(error.ActivatedExtensions));
        Assert.IsAssignableFrom<FrozenSet<string>>(error.ActivatedExtensions);
        Assert.Equal(1, invoker.Disposals);
    }

    [Fact]
    public async Task StreamingError_AfterAnEventKeepsTheInitialSnapshot()
    {
        var error = GrpcErrorMapping.ToRpcException(new A2AException("missing task", A2AErrorCode.TaskNotFound));
        error.Trailers.Add("a2a-extensions", OtherUri);
        var invoker = new FailingCallInvoker(new Metadata { { "a2a-extensions", ExtensionUri } }, error, yieldEvent: true);
        using var client = new A2AGrpcClient(new Protos.A2AService.A2AServiceClient(invoker));
        var events = 0;

        var exception = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var response in client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "task" }))
            {
                Assert.Equal("task", response.Task!.Id);
                events++;
            }
        });

        Assert.Equal(1, events);
        Assert.Equal(ExtensionUri, Assert.Single(exception.ActivatedExtensions));
        Assert.Equal(1, invoker.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellation_RemainsOperationCanceledAndDisposesTheCall(bool streaming)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var rpcError = new RpcException(new Status(StatusCode.Cancelled, "cancelled"));
        var invoker = new FailingCallInvoker(new Metadata { { "a2a-extensions", ExtensionUri } }, rpcError);
        using var client = new A2AGrpcClient(new Protos.A2AService.A2AServiceClient(invoker));

        var error = streaming
            ? await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "task" }, cancellation.Token)) { }
            })
            : await Assert.ThrowsAsync<OperationCanceledException>(() =>
                client.GetTaskAsync(new GetTaskRequest { Id = "task" }, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, invoker.Disposals);
    }

    [Fact]
    public void TrailerDecoder_UsesExactIdentifiersAndDoesNotInspectRequestedMetadata()
    {
        var error = GrpcErrorMapping.ToRpcException(new A2AException("missing task", A2AErrorCode.TaskNotFound));
        error.Trailers.Add("a2a-extensions", $"{ExtensionUri}, {OtherUri}");
        error.Trailers.Add("a2a-extensions", ExtensionUri.ToUpperInvariant());
        error.Trailers.Add("a2a-extensions", ExtensionUri);

        var decoded = GrpcErrorMapping.ToA2AException(error);

        Assert.Equal(3, decoded.ActivatedExtensions.Count);
        Assert.Contains(ExtensionUri, decoded.ActivatedExtensions);
        Assert.Contains(OtherUri, decoded.ActivatedExtensions);
        Assert.Contains(ExtensionUri.ToUpperInvariant(), decoded.ActivatedExtensions);
    }

    private sealed class FailingCallInvoker(Metadata headers, RpcException error, bool failHeaders = false, bool yieldEvent = false) : CallInvoker
    {
        public int Disposals { get; private set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            => new(Task.FromException<TResponse>(error), Headers(), () => error.Status, () => error.Trailers, () => Disposals++);

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            => new(new FailingReader<TResponse>(error, yieldEvent), Headers(), () => error.Status, () => error.Trailers, () => Disposals++);

        private Task<Metadata> Headers() => failHeaders ? Task.FromException<Metadata>(error) : Task.FromResult(headers);

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }

    private sealed class FailingReader<TResponse>(RpcException error, bool yieldEvent) : IAsyncStreamReader<TResponse> where TResponse : class
    {
        private bool _yielded;
        public TResponse Current { get; private set; } = default!;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (yieldEvent && !_yielded)
            {
                _yielded = true;
                Current = (TResponse)(object)new Protos.StreamResponse
                {
                    Task = new Protos.Task { Id = "task", ContextId = "context", Status = new Protos.TaskStatus() },
                };
                return Task.FromResult(true);
            }
            return Task.FromException<bool>(error);
        }
    }
}

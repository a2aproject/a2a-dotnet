using System.Net;
using System.Text;
using System.Text.Json;

namespace A2A.UnitTests.Client;

public sealed class A2AExtensionErrorMetadataTests
{
    private const string ExtensionUri = "https://example.org/extensions/debate/v1";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UsageExample_ProtocolErrorPreservesActivationAndErrorCode(bool jsonRpc)
    {
        using var http = new HttpClient(new Handler(_ => ErrorResponse(jsonRpc, ExtensionUri)));
        using var client = CreateClient(jsonRpc, http);

        var error = await Assert.ThrowsAsync<A2AException>(() => client.Client.GetTaskAsync(new GetTaskRequest { Id = "missing" }));

        Assert.Equal(A2AErrorCode.TaskNotFound, error.ErrorCode);
        Assert.Equal(ExtensionUri, Assert.Single(error.ActivatedExtensions));
        Assert.Throws<NotSupportedException>(() => ((ISet<string>)error.ActivatedExtensions).Clear());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegacyPeerWithoutMetadata_ReturnsAnEmptySnapshot(bool jsonRpc)
    {
        using var http = new HttpClient(new Handler(_ => ErrorResponse(jsonRpc)));
        using var client = CreateClient(jsonRpc, http);
        var error = await Assert.ThrowsAsync<A2AException>(() => client.Client.GetTaskAsync(new GetTaskRequest { Id = "missing" }));
        Assert.Empty(error.ActivatedExtensions);
        Assert.Equal(A2AErrorCode.TaskNotFound, error.ErrorCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MalformedMetadata_DoesNotReplaceTheOriginalProtocolError(bool jsonRpc)
    {
        using var http = new HttpClient(new Handler(_ => ErrorResponse(jsonRpc, $"{ExtensionUri}, invalid identifier")));
        using var client = CreateClient(jsonRpc, http);
        var error = await Assert.ThrowsAsync<A2AException>(() => client.Client.GetTaskAsync(new GetTaskRequest { Id = "missing" }));
        Assert.Equal(A2AErrorCode.TaskNotFound, error.ErrorCode);
        Assert.Empty(error.ActivatedExtensions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResponseMetadata_IsParsedExactlyAndDoesNotComeFromRequestHeaders(bool jsonRpc)
    {
        using var http = new HttpClient(new Handler(_ => ErrorResponse(jsonRpc,
            $" , {ExtensionUri}, {ExtensionUri}, {ExtensionUri.ToUpperInvariant()}, https://example.org/extensions/debate/v2, ")));
        http.DefaultRequestHeaders.Add("A2A-Extensions", "https://example.org/extensions/request-only/v1");
        using var client = CreateClient(jsonRpc, http);
        var error = await Assert.ThrowsAsync<A2AException>(() => client.Client.GetTaskAsync(new GetTaskRequest { Id = "missing" }));
        Assert.Equal(3, error.ActivatedExtensions.Count);
        Assert.Contains(ExtensionUri, error.ActivatedExtensions);
        Assert.DoesNotContain("https://example.org/extensions/request-only/v1", error.ActivatedExtensions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StreamingProtocolError_PreservesTheInitialSnapshotAfterAnEvent(bool jsonRpc)
    {
        var streamEvent = new StreamResponse { Message = new Message { MessageId = "agent-1", Role = Role.Agent, Parts = [Part.FromText("reply")] } };
        var first = jsonRpc ? JsonSerializer.Serialize(JsonRpcResponse.CreateJsonRpcResponse("1", streamEvent), A2AJsonUtilities.DefaultOptions)
            : JsonSerializer.Serialize(streamEvent, A2AJsonUtilities.DefaultOptions);
        var second = jsonRpc ? "{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"error\":{\"code\":-32001,\"message\":\"task not found\"}}"
            : "{\"error\":{\"code\":-32001,\"message\":\"task not found\"}}";
        using var http = new HttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"data: {first}\n\ndata: {second}\n\n", Encoding.UTF8, "text/event-stream") };
            response.Headers.Add("A2A-Extensions", ExtensionUri);
            return response;
        }));
        using var client = CreateClient(jsonRpc, http);
        var events = 0;

        var error = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in client.Client.SendStreamingMessageAsync(MessageRequest())) events++;
        });

        Assert.Equal(1, events);
        Assert.Equal(A2AErrorCode.TaskNotFound, error.ErrorCode);
        Assert.Equal(ExtensionUri, Assert.Single(error.ActivatedExtensions));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StreamingRejectionBeforeFirstEvent_StillPreservesActivation(bool jsonRpc)
    {
        using var http = new HttpClient(new Handler(_ => ErrorResponse(jsonRpc, ExtensionUri)));
        using var client = CreateClient(jsonRpc, http);
        var error = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in client.Client.SendStreamingMessageAsync(MessageRequest())) { }
        });
        Assert.Equal(ExtensionUri, Assert.Single(error.ActivatedExtensions));
        Assert.Equal(A2AErrorCode.TaskNotFound, error.ErrorCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentCalls_DoNotShareErrorMetadata(bool jsonRpc)
    {
        var next = 0;
        using var http = new HttpClient(new Handler(_ => ErrorResponse(jsonRpc, Interlocked.Increment(ref next) % 2 == 0 ? ExtensionUri : null)));
        using var client = CreateClient(jsonRpc, http);
        var errors = await Task.WhenAll(Enumerable.Range(0, 40).Select(async _ =>
            await Assert.ThrowsAsync<A2AException>(() => client.Client.GetTaskAsync(new GetTaskRequest { Id = "missing" }))));
        Assert.Equal(20, errors.Count(error => error.ActivatedExtensions.Contains(ExtensionUri)));
        Assert.Equal(20, errors.Count(error => error.ActivatedExtensions.Count == 0));
        Assert.Equal(40, errors.Distinct().Count());
    }

    [Fact]
    public void ServerCreatedExceptions_DefaultToEmptyMetadata()
        => Assert.Empty(new A2AException("error", A2AErrorCode.InvalidParams).ActivatedExtensions);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task CancellationWhileReadingAnErrorBody_IsNotTurnedIntoAProtocolError(bool jsonRpc, bool streaming)
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(_ =>
        {
            var response = ErrorResponse(jsonRpc, ExtensionUri);
            response.Content.Dispose();
            response.Content = new StreamContent(new CancelOnReadStream(cancellation));
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return response;
        }));
        using var client = CreateClient(jsonRpc, http);

        if (streaming)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in client.Client.SendStreamingMessageAsync(MessageRequest(), cancellation.Token)) { }
            });
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.Client.GetTaskAsync(new GetTaskRequest { Id = "missing" }, cancellation.Token));
        }
        Assert.True(cancellation.IsCancellationRequested);
    }

    private static SendMessageRequest MessageRequest() => new()
    {
        Message = new Message { MessageId = "client-1", Role = Role.User, Parts = [Part.FromText("hello")] },
    };

    private static HttpResponseMessage ErrorResponse(bool jsonRpc, string? extensions = null)
    {
        var response = new HttpResponseMessage(jsonRpc ? HttpStatusCode.OK : HttpStatusCode.NotFound)
        {
            Content = new StringContent(jsonRpc
                ? "{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"error\":{\"code\":-32001,\"message\":\"task not found\"}}"
                : "{\"error\":{\"code\":404,\"status\":\"NOT_FOUND\",\"message\":\"task not found\",\"details\":[{\"reason\":\"TASK_NOT_FOUND\",\"domain\":\"a2a-protocol.org\"}]}}", Encoding.UTF8, "application/json"),
        };
        if (extensions is not null) response.Headers.TryAddWithoutValidation("A2A-Extensions", extensions);
        return response;
    }

    private static ClientScope CreateClient(bool jsonRpc, HttpClient http)
        => jsonRpc ? new ClientScope(new A2AClient(new Uri("https://example.org/rpc"), http))
            : new ClientScope(new A2AHttpJsonClient(new Uri("https://example.org/http"), http));

    private sealed class ClientScope(IA2AClient client) : IDisposable
    {
        public IA2AClient Client { get; } = client;
        public void Dispose() => ((IDisposable)Client).Dispose();
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    private sealed class CancelOnReadStream(CancellationTokenSource cancellation) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await cancellation.CancelAsync();
            throw new OperationCanceledException(cancellation.Token);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

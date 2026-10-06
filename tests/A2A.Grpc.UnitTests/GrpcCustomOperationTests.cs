namespace A2A.Grpc.UnitTests;

using A2A;
using A2A.Grpc.Extensions.Protos;
using global::Google.Protobuf;
using global::Grpc.Core;
using global::Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class GrpcCustomOperationTests : IAsyncLifetime
{
    private WebApplication? _app;
    private GrpcChannel? _channel;
    private A2AExtensionService.A2AExtensionServiceClient? _client;
    private A2ACustomOperationRegistry? _registry;

    public async Task InitializeAsync()
    {
        var registryBuilder = new A2ACustomOperationRegistryBuilder();
        registryBuilder.Map<GrpcCustomRequest, GrpcCustomResult>(
            new A2AOperationId("https://example.test/operations#unary"),
            (request, _) => ValueTask.FromResult(
                new GrpcCustomResult($"{request.Value}-response")),
            GrpcCustomJsonContext.Default.GrpcCustomRequest,
            GrpcCustomJsonContext.Default.GrpcCustomResult);
        registryBuilder.MapStreaming<GrpcCustomRequest, GrpcCustomResult>(
            new A2AOperationId("https://example.test/operations#stream"),
            StreamResults,
            GrpcCustomJsonContext.Default.GrpcCustomRequest,
            GrpcCustomJsonContext.Default.GrpcCustomResult);
        registryBuilder.MapStreaming<GrpcCustomRequest, GrpcCustomResult>(
            new A2AOperationId("https://example.test/operations#cancel"),
            WaitForCancellation,
            GrpcCustomJsonContext.Default.GrpcCustomRequest,
            GrpcCustomJsonContext.Default.GrpcCustomResult);
        registryBuilder.Map<GrpcCustomRequest, GrpcCustomResult>(
            new A2AOperationId("https://example.test/operations#a2a-error"),
            (_, _) => throw new A2AException("invalid extension request", A2AErrorCode.InvalidParams),
            GrpcCustomJsonContext.Default.GrpcCustomRequest,
            GrpcCustomJsonContext.Default.GrpcCustomResult);
        registryBuilder.Map<GrpcCustomRequest, GrpcCustomResult>(
            new A2AOperationId("https://example.test/operations#unexpected"),
            (_, _) => throw new InvalidOperationException("sensitive detail"),
            GrpcCustomJsonContext.Default.GrpcCustomRequest,
            GrpcCustomJsonContext.Default.GrpcCustomResult);
        _registry = registryBuilder.Build();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddA2AGrpcCustomOperations(_registry);

        _app = builder.Build();
        _app.MapGrpcA2ACustomOperations();
        await _app.StartAsync();

        var testServer = _app.GetTestServer();
        _channel = GrpcChannel.ForAddress(
            testServer.BaseAddress,
            new GrpcChannelOptions { HttpHandler = testServer.CreateHandler() });
        _client = new A2AExtensionService.A2AExtensionServiceClient(_channel);
    }

    public async Task DisposeAsync()
    {
        _channel?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task InvokeExtensionOperation_DispatchesUnaryOperation()
    {
        var response = await Client.InvokeExtensionOperationAsync(
            CreateRequest(
                "https://example.test/operations#unary",
                new GrpcCustomRequest("request")));

        var result = JsonSerializer.Deserialize(
            response.Payload.Span,
            GrpcCustomJsonContext.Default.GrpcCustomResult);
        Assert.Equal("request-response", result!.Value);
    }

    [Fact]
    public async Task InvokeStreamingExtensionOperation_DispatchesStreamingOperation()
    {
        using var call = Client.InvokeStreamingExtensionOperation(
            CreateRequest(
                "https://example.test/operations#stream",
                new GrpcCustomRequest("event")));
        var results = new List<GrpcCustomResult>();

        await foreach (var response in call.ResponseStream.ReadAllAsync())
        {
            results.Add(JsonSerializer.Deserialize(
                response.Payload.Span,
                GrpcCustomJsonContext.Default.GrpcCustomResult)!);
        }

        Assert.Collection(
            results,
            result => Assert.Equal("event-1", result.Value),
            result => Assert.Equal("event-2", result.Value));
    }

    [Fact]
    public async Task InvokeExtensionOperation_RejectsUnknownOperation()
    {
        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await Client.InvokeExtensionOperationAsync(
                CreateRequest(
                    "https://example.test/operations#unknown",
                    new GrpcCustomRequest("request"))));

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task InvokeExtensionOperation_RejectsStreamingOperation()
    {
        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await Client.InvokeExtensionOperationAsync(
                CreateRequest(
                    "https://example.test/operations#stream",
                    new GrpcCustomRequest("request"))));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task InvokeStreamingExtensionOperation_RejectsUnaryOperation()
    {
        using var call = Client.InvokeStreamingExtensionOperation(
            CreateRequest(
                "https://example.test/operations#unary",
                new GrpcCustomRequest("request")));

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await call.ResponseStream.MoveNext());

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task InvokeExtensionOperation_RejectsMalformedJson()
    {
        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await Client.InvokeExtensionOperationAsync(new ExtensionOperationRequest
            {
                OperationId = "https://example.test/operations#unary",
                Payload = ByteString.CopyFromUtf8("{"),
            }));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task InvokeExtensionOperation_MapsA2AException()
    {
        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await Client.InvokeExtensionOperationAsync(
                CreateRequest(
                    "https://example.test/operations#a2a-error",
                    new GrpcCustomRequest("request"))));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        Assert.Contains("invalid extension request", exception.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeExtensionOperation_SanitizesUnexpectedException()
    {
        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await Client.InvokeExtensionOperationAsync(
                CreateRequest(
                    "https://example.test/operations#unexpected",
                    new GrpcCustomRequest("request"))));

        Assert.Equal(StatusCode.Internal, exception.StatusCode);
        Assert.Equal("An internal error occurred.", exception.Status.Detail);
        Assert.DoesNotContain("sensitive detail", exception.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeStreamingExtensionOperation_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var call = Client.InvokeStreamingExtensionOperation(
            CreateRequest(
                "https://example.test/operations#cancel",
                new GrpcCustomRequest("request")),
            cancellationToken: cancellation.Token);

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await call.ResponseStream.MoveNext());

        Assert.Equal(StatusCode.Cancelled, exception.StatusCode);
    }

    private A2AExtensionService.A2AExtensionServiceClient Client => _client!;

    private static ExtensionOperationRequest CreateRequest(
        string operationId,
        GrpcCustomRequest request) =>
        new()
        {
            OperationId = operationId,
            Payload = ByteString.CopyFrom(
                JsonSerializer.SerializeToUtf8Bytes(
                    request,
                    GrpcCustomJsonContext.Default.GrpcCustomRequest)),
        };

    private static async IAsyncEnumerable<GrpcCustomResult> StreamResults(
        GrpcCustomRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new GrpcCustomResult($"{request.Value}-1");
        await Task.Yield();
        yield return new GrpcCustomResult($"{request.Value}-2");
    }

    private static async IAsyncEnumerable<GrpcCustomResult> WaitForCancellation(
        GrpcCustomRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        yield return new GrpcCustomResult(request.Value);
    }
}

internal sealed record GrpcCustomRequest(string Value);

internal sealed record GrpcCustomResult(string Value);

[JsonSerializable(typeof(GrpcCustomRequest))]
[JsonSerializable(typeof(GrpcCustomResult))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class GrpcCustomJsonContext : JsonSerializerContext;

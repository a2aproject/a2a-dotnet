namespace A2A.Grpc.UnitTests;

using System.Text.Json;
using System.Text.Json.Serialization;
using A2A;
using A2A.Grpc;
using A2A.Grpc.AspNetCore;
using global::Grpc.Core;
using global::Grpc.Net.Client;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// End-to-end tests exercising <c>MapGrpcA2AExtensions</c> (the generic gRPC envelope for custom/extension
/// operations) over an in-memory ASP.NET Core <see cref="TestServer"/>.
/// </summary>
public sealed class GrpcExtensionOperationTests : IAsyncLifetime
{
    private static readonly A2AOperationId UnaryOperationId =
        new("https://example.com/extensions/test#grpc-unary");

    private static readonly A2AOperationId StreamingOperationId =
        new("https://example.com/extensions/test#grpc-streaming");

    private readonly FakeRequestHandler _handler = new();
    private WebApplication? _app;
    private GrpcChannel? _channel;
    private Protos.A2AExtensionService.A2AExtensionServiceClient? _client;
    private A2AException? _operationError;

    private Protos.A2AExtensionService.A2AExtensionServiceClient Client => _client!;

    public async Task InitializeAsync()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var unaryOperation = operationBuilder.DefineUnary<CustomRequest, CustomResult>(UnaryOperationId);
        var streamingOperation = operationBuilder.DefineStreaming<CustomRequest, CustomStreamEvent>(
            StreamingOperationId);
        var operationCatalog = operationBuilder.Build();

        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                unaryOperation,
                (_, request, _) =>
                {
                    if (_operationError is { } error)
                    {
                        throw error;
                    }

                    return ValueTask.FromResult(new CustomResult("handled:" + request.Value));
                })
            .MapStreaming(streamingOperation, static (_, request, cancellationToken) => YieldStreamEventsAsync(request, cancellationToken))
            .Build(operationCatalog);

        var bindings = new A2AGrpcExtensionOperationBindingBuilder()
            .Map(unaryOperation, CustomJsonContext.Default.CustomRequest, CustomJsonContext.Default.CustomResult)
            .MapStreaming(
                streamingOperation,
                CustomJsonContext.Default.CustomRequest,
                CustomJsonContext.Default.CustomStreamEvent)
            .Build(operationCatalog);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGrpc();
        builder.Services.AddSingleton<IA2ARequestHandler>(_handler);
        builder.Services.AddA2AGrpcExtensions(handlers, bindings);

        _app = builder.Build();
        _app.MapGrpcA2AExtensions();
        await _app.StartAsync();

        var testServer = _app.GetTestServer();
        _channel = GrpcChannel.ForAddress(
            testServer.BaseAddress,
            new GrpcChannelOptions { HttpHandler = testServer.CreateHandler() });
        _client = new Protos.A2AExtensionService.A2AExtensionServiceClient(_channel);
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
    public async Task InvokeExtensionOperation_RoundTripsThroughHandler()
    {
        var request = new Protos.ExtensionOperationRequest
        {
            OperationId = UnaryOperationId.Value,
            Payload = ByteString.CopyFrom(
                JsonSerializer.SerializeToUtf8Bytes(
                    new CustomRequest("hello"),
                    CustomJsonContext.Default.CustomRequest)),
        };

        var response = await Client.InvokeExtensionOperationAsync(request);

        var result = JsonSerializer.Deserialize(
            response.Payload.Span,
            CustomJsonContext.Default.CustomResult);
        Assert.Equal("handled:hello", result!.Value);
    }

    [Fact]
    public async Task InvokeStreamingExtensionOperation_YieldsAllEvents()
    {
        var request = new Protos.ExtensionOperationRequest
        {
            OperationId = StreamingOperationId.Value,
            Payload = ByteString.CopyFrom(
                JsonSerializer.SerializeToUtf8Bytes(
                    new CustomRequest("go"),
                    CustomJsonContext.Default.CustomRequest)),
        };

        using var call = Client.InvokeStreamingExtensionOperation(request);
        var received = new List<string>();
        await foreach (var streamEvent in call.ResponseStream.ReadAllAsync())
        {
            var value = JsonSerializer.Deserialize(
                streamEvent.Payload.Span,
                CustomJsonContext.Default.CustomStreamEvent);
            received.Add(value!.Value);
        }

        Assert.Equal(["go-0", "go-1"], received);
    }

    [Fact]
    public async Task InvokeExtensionOperation_UnknownOperationId_ThrowsMethodNotFound()
    {
        var request = new Protos.ExtensionOperationRequest
        {
            OperationId = "https://example.com/extensions/test#does-not-exist",
            Payload = ByteString.CopyFrom(
                JsonSerializer.SerializeToUtf8Bytes(
                    new CustomRequest("hello"),
                    CustomJsonContext.Default.CustomRequest)),
        };

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => Client.InvokeExtensionOperationAsync(request).ResponseAsync);

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task InvokeExtensionOperation_WrongKind_ThrowsInvalidArgument()
    {
        var request = new Protos.ExtensionOperationRequest
        {
            OperationId = StreamingOperationId.Value,
            Payload = ByteString.CopyFrom(
                JsonSerializer.SerializeToUtf8Bytes(
                    new CustomRequest("hello"),
                    CustomJsonContext.Default.CustomRequest)),
        };

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => Client.InvokeExtensionOperationAsync(request).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task InvokeExtensionOperation_HandlerA2AException_IsSurfacedWithErrorCode()
    {
        _operationError = new A2AException("denied", A2AErrorCode.UnsupportedOperation);
        var request = new Protos.ExtensionOperationRequest
        {
            OperationId = UnaryOperationId.Value,
            Payload = ByteString.CopyFrom(
                JsonSerializer.SerializeToUtf8Bytes(
                    new CustomRequest("hello"),
                    CustomJsonContext.Default.CustomRequest)),
        };

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => Client.InvokeExtensionOperationAsync(request).ResponseAsync);

        var mapped = GrpcErrorMapping.ToA2AException(exception);
        Assert.Equal(A2AErrorCode.UnsupportedOperation, mapped.ErrorCode);
    }

    private static async IAsyncEnumerable<CustomStreamEvent> YieldStreamEventsAsync(
        CustomRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < 2; i++)
        {
            await Task.Yield();
            yield return new CustomStreamEvent($"{request.Value}-{i}");
        }
    }

    internal sealed record CustomRequest([property: JsonPropertyName("value")] string Value);

    internal sealed record CustomResult([property: JsonPropertyName("value")] string Value);

    internal sealed record CustomStreamEvent([property: JsonPropertyName("value")] string Value);
}

[JsonSerializable(typeof(GrpcExtensionOperationTests.CustomRequest))]
[JsonSerializable(typeof(GrpcExtensionOperationTests.CustomResult))]
[JsonSerializable(typeof(GrpcExtensionOperationTests.CustomStreamEvent))]
internal sealed partial class CustomJsonContext : JsonSerializerContext;

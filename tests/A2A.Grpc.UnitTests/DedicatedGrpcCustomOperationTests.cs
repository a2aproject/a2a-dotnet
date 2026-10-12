namespace A2A.Grpc.UnitTests;

using A2A;
using A2A.Tests.GrpcCustom.Protos;
using global::Grpc.Core;
using global::Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

public sealed class DedicatedGrpcCustomOperationTests : IAsyncLifetime
{
    private WebApplication? _app;
    private GrpcChannel? _channel;
    private TestCustomOperations.TestCustomOperationsClient? _client;

    public async Task InitializeAsync()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var unary = builder.Map<DedicatedRequest, DedicatedResult>(
            new A2AOperationId("https://example.test/operations#dedicated-resume"),
            (request, _) => ValueTask.FromResult(
                new DedicatedResult($"{request.Value}-response")),
            DedicatedGrpcJsonContext.Default.DedicatedRequest,
            DedicatedGrpcJsonContext.Default.DedicatedResult,
            Validate);
        var streaming = builder.MapStreaming<DedicatedRequest, DedicatedResult>(
            new A2AOperationId("https://example.test/operations#dedicated-watch"),
            StreamResults,
            DedicatedGrpcJsonContext.Default.DedicatedRequest,
            DedicatedGrpcJsonContext.Default.DedicatedResult,
            Validate);
        var operations = new DedicatedOperations(builder.Build(), unary, streaming);

        var appBuilder = WebApplication.CreateBuilder();
        appBuilder.WebHost.UseTestServer();
        appBuilder.Services.AddGrpc();
        appBuilder.Services.AddSingleton(operations);

        _app = appBuilder.Build();
        _app.MapGrpcService<DedicatedGrpcService>();
        await _app.StartAsync();

        var testServer = _app.GetTestServer();
        _channel = GrpcChannel.ForAddress(
            testServer.BaseAddress,
            new GrpcChannelOptions { HttpHandler = testServer.CreateHandler() });
        _client = new TestCustomOperations.TestCustomOperationsClient(_channel);
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
    public async Task DedicatedUnaryService_InvokesTypedRegistryHandle()
    {
        var response = await _client!.ResumeAsync(new TestCustomRequest { Value = "request" });

        Assert.Equal("request-response", response.Value);
    }

    [Fact]
    public async Task DedicatedStreamingService_InvokesTypedRegistryHandle()
    {
        using var call = _client!.Watch(new TestCustomRequest { Value = "event" });
        var responses = new List<TestCustomResponse>();

        await foreach (var response in call.ResponseStream.ReadAllAsync())
        {
            responses.Add(response);
        }

        Assert.Collection(
            responses,
            response => Assert.Equal("event-1", response.Value),
            response => Assert.Equal("event-2", response.Value));
    }

    [Fact]
    public async Task DedicatedService_PropagatesRegistryValidation()
    {
        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await _client!.ResumeAsync(new TestCustomRequest()));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        Assert.Contains("required", exception.Status.Detail, StringComparison.Ordinal);
    }

    private static void Validate(DedicatedRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
        {
            throw new A2AException("Value is required.", A2AErrorCode.InvalidParams);
        }
    }

    private static async IAsyncEnumerable<DedicatedResult> StreamResults(
        DedicatedRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new DedicatedResult($"{request.Value}-1");
        await Task.Yield();
        yield return new DedicatedResult($"{request.Value}-2");
    }
}

internal sealed record DedicatedOperations(
    A2ACustomOperationRegistry Registry,
    A2ACustomOperation<DedicatedRequest, DedicatedResult> Unary,
    A2AStreamingCustomOperation<DedicatedRequest, DedicatedResult> Streaming);

internal sealed class DedicatedGrpcService(DedicatedOperations operations)
    : TestCustomOperations.TestCustomOperationsBase
{
    public override async Task<TestCustomResponse> Resume(
        TestCustomRequest request,
        ServerCallContext context)
    {
        try
        {
            var result = await operations.Registry.InvokeAsync(
                operations.Unary,
                new DedicatedRequest(request.Value),
                context.CancellationToken);
            return new TestCustomResponse { Value = result.Value };
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }

    public override async Task Watch(
        TestCustomRequest request,
        IServerStreamWriter<TestCustomResponse> responseStream,
        ServerCallContext context)
    {
        try
        {
            await foreach (var streamEvent in operations.Registry.InvokeStreamingAsync(
                operations.Streaming,
                new DedicatedRequest(request.Value),
                context.CancellationToken))
            {
                await responseStream.WriteAsync(
                    new TestCustomResponse { Value = streamEvent.Value });
            }
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }
}

internal sealed record DedicatedRequest(string Value);

internal sealed record DedicatedResult(string Value);

[JsonSerializable(typeof(DedicatedRequest))]
[JsonSerializable(typeof(DedicatedResult))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class DedicatedGrpcJsonContext : JsonSerializerContext;

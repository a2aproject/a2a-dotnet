namespace A2A.Grpc.UnitTests;

using A2A.Grpc.AspNetCore;
using A2A.Tests.GrpcCustom.Protos;
using global::Grpc.AspNetCore.Server;
using global::Grpc.Core;
using global::Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

public sealed class DedicatedGrpcExtensionHostingTests
{
    private const string ExtensionUri = "https://example.org/extensions/debate/v1";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DedicatedService_UsesTheNegotiatedContextForTypedInvocation(bool streaming)
    {
        var seen = new List<A2ARequestContext>();
        var operations = CreateOperations(context => seen.Add(context.GetRequiredFeature<A2ARequestContext>()));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(operations);
        builder.Services.AddGrpc().WithA2AExtensions<ExtensionAwareDedicatedGrpcService>(new A2AGrpcExtensionOptions(operations.Registry));
        await using var app = builder.Build();
        app.MapGrpcService<ExtensionAwareDedicatedGrpcService>();
        await app.StartAsync();
        var testServer = app.GetTestServer();
        using var channel = GrpcChannel.ForAddress(testServer.BaseAddress, new GrpcChannelOptions { HttpHandler = testServer.CreateHandler() });
        var client = new TestCustomOperations.TestCustomOperationsClient(channel);
        var headers = new Metadata { { "a2a-extensions", ExtensionUri } };

        if (streaming)
        {
            using var call = client.Watch(new TestCustomRequest { Value = "debate-42" }, headers);
            Assert.True(await call.ResponseStream.MoveNext());
            Assert.Equal("debate-42", call.ResponseStream.Current.Value);
            Assert.False(await call.ResponseStream.MoveNext());
            Assert.Equal(ExtensionUri, (await call.ResponseHeadersAsync).GetValue("a2a-extensions"));
            Assert.Equal("dedicated-service", (await call.ResponseHeadersAsync).GetValue("x-host-response"));
        }
        else
        {
            using var call = client.ResumeAsync(new TestCustomRequest { Value = "debate-42" }, headers);
            Assert.Equal("debate-42", (await call.ResponseAsync).Value);
            Assert.Equal(ExtensionUri, (await call.ResponseHeadersAsync).GetValue("a2a-extensions"));
            Assert.Equal("dedicated-service", (await call.ResponseHeadersAsync).GetValue("x-host-response"));
        }

        Assert.Equal(ExtensionUri, Assert.Single(Assert.Single(seen).ActivatedExtensions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectTypedInvocation_CannotBypassNegotiationWithOnlyAMetadataHeader(bool streaming)
    {
        var operations = CreateOperations(_ => Assert.Fail("The handler must not run."));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(operations);
        builder.Services.AddGrpc();
        await using var app = builder.Build();
        app.MapGrpcService<ExtensionAwareDedicatedGrpcService>();
        await app.StartAsync();
        var testServer = app.GetTestServer();
        using var channel = GrpcChannel.ForAddress(testServer.BaseAddress, new GrpcChannelOptions { HttpHandler = testServer.CreateHandler() });
        var client = new TestCustomOperations.TestCustomOperationsClient(channel);
        var headers = new Metadata { { "a2a-extensions", ExtensionUri } };
        RpcException error;
        if (streaming)
        {
            using var call = client.Watch(new TestCustomRequest { Value = "debate-42" }, headers);
            error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext());
        }
        else
        {
            using var call = client.ResumeAsync(new TestCustomRequest { Value = "debate-42" }, headers);
            error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
        }

        Assert.Equal(A2AErrorCode.ExtensionSupportRequired, GrpcErrorMapping.ToA2AException(error).ErrorCode);
    }

    [Fact]
    public async Task AnUnrelatedGrpcService_RemainsNegotiationFree()
    {
        var operations = CreateOperations(ownerRequired: false, attachOwner: false);
        var extensionBuilder = new A2ACustomOperationRegistryBuilder();
        extensionBuilder.RegisterExtension(new AgentExtension { Uri = ExtensionUri, Required = true });
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddA2AGrpc(new A2AGrpcExtensionOptions(extensionBuilder.Build()));
        builder.Services.AddSingleton(operations);
        await using var app = builder.Build();
        app.MapGrpcService<DedicatedGrpcService>();
        await app.StartAsync();
        var testServer = app.GetTestServer();
        using var channel = GrpcChannel.ForAddress(testServer.BaseAddress, new GrpcChannelOptions { HttpHandler = testServer.CreateHandler() });
        var client = new TestCustomOperations.TestCustomOperationsClient(channel);
        using var call = client.ResumeAsync(new TestCustomRequest { Value = "unrelated" },
            new Metadata { { "a2a-extensions", "not an extension identifier" } });

        Assert.Equal("unrelated", (await call.ResponseAsync).Value);
        Assert.Null((await call.ResponseHeadersAsync).GetValue("a2a-extensions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictingConfiguration_IsRejectedBeforeServingCalls(bool differentPolicy)
    {
        var registry = new A2ACustomOperationRegistryBuilder().Build();
        var first = new A2AGrpcExtensionOptions(registry);
        var second = differentPolicy
            ? new A2AGrpcExtensionOptions(registry) { CanActivate = (_, _, _) => ValueTask.FromResult(false) }
            : new A2AGrpcExtensionOptions(new A2ACustomOperationRegistryBuilder().Build());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGrpc().WithA2AExtensions<ExtensionAwareDedicatedGrpcService>(first)
            .WithA2AExtensions<ExtensionAwareDedicatedGrpcService>(second);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<IOptions<GrpcServiceOptions<ExtensionAwareDedicatedGrpcService>>>().Value);
    }

    private static DedicatedOperations CreateOperations(Action<A2ACustomOperationContext>? onInvoke = null,
        bool ownerRequired = false, bool attachOwner = true)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri, Required = ownerRequired });
        A2AContextualCustomOperationHandler<DedicatedRequest, DedicatedResult> handler = (context, request, _) =>
        {
            onInvoke?.Invoke(context);
            return ValueTask.FromResult(new DedicatedResult(request.Value));
        };
        var unary = attachOwner
            ? builder.Map(extension, new A2AOperationId("https://example.org/operations#resume"), handler,
                DedicatedGrpcJsonContext.Default.DedicatedRequest, DedicatedGrpcJsonContext.Default.DedicatedResult)
            : builder.Map(new A2AOperationId("https://example.org/operations#resume"), handler,
                DedicatedGrpcJsonContext.Default.DedicatedRequest, DedicatedGrpcJsonContext.Default.DedicatedResult);
        var streaming = attachOwner
            ? builder.MapStreaming(extension, new A2AOperationId("https://example.org/operations#watch"), Stream,
                DedicatedGrpcJsonContext.Default.DedicatedRequest, DedicatedGrpcJsonContext.Default.DedicatedResult)
            : builder.MapStreaming(new A2AOperationId("https://example.org/operations#watch"), Stream,
                DedicatedGrpcJsonContext.Default.DedicatedRequest, DedicatedGrpcJsonContext.Default.DedicatedResult);
        return new DedicatedOperations(builder.Build(), unary, streaming);

        async IAsyncEnumerable<DedicatedResult> Stream(A2ACustomOperationContext context, DedicatedRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onInvoke?.Invoke(context);
            yield return new DedicatedResult(request.Value);
            await Task.Yield();
        }
    }
}

internal sealed class ExtensionAwareDedicatedGrpcService(DedicatedOperations operations)
    : TestCustomOperations.TestCustomOperationsBase
{
    public override async Task<TestCustomResponse> Resume(TestCustomRequest request, ServerCallContext context)
    {
        try
        {
            await context.WriteResponseHeadersAsync(new Metadata { { "x-host-response", "dedicated-service" } });
            var result = await operations.Registry.InvokeAsync(operations.Unary,
                context.CreateA2AOperationContext(), new DedicatedRequest(request.Value), context.CancellationToken);
            return new TestCustomResponse { Value = result.Value };
        }
        catch (A2AException exception) { throw GrpcErrorMapping.ToRpcException(exception); }
    }

    public override async Task Watch(TestCustomRequest request, IServerStreamWriter<TestCustomResponse> responseStream,
        ServerCallContext context)
    {
        try
        {
            await context.WriteResponseHeadersAsync(new Metadata { { "x-host-response", "dedicated-service" } });
            await foreach (var result in operations.Registry.InvokeStreamingAsync(operations.Streaming,
                context.CreateA2AOperationContext(), new DedicatedRequest(request.Value), context.CancellationToken))
            {
                await responseStream.WriteAsync(new TestCustomResponse { Value = result.Value });
            }
        }
        catch (A2AException exception) { throw GrpcErrorMapping.ToRpcException(exception); }
    }
}

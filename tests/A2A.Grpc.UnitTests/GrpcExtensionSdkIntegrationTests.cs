namespace A2A.Grpc.UnitTests;

using A2A.Grpc.AspNetCore;
using global::Grpc.Core;
using global::Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

public sealed class GrpcExtensionSdkIntegrationTests
{
    private const string ExtensionUri = "https://example.org/extensions/debate/v1";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SdkReceivesActivationOnStandardUnaryAndStreamingErrors(bool streaming)
    {
        await using var host = await Host.StartAsync();
        host.Handler.Error = new A2AException("missing task", A2AErrorCode.TaskNotFound);

        var error = streaming
            ? await Assert.ThrowsAsync<A2AException>(async () =>
            {
                await foreach (var _ in host.Client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "missing" })) { }
            })
            : await Assert.ThrowsAsync<A2AException>(() => host.Client.GetTaskAsync(new GetTaskRequest { Id = "missing" }));

        Assert.Equal(A2AErrorCode.TaskNotFound, error.ErrorCode);
        Assert.IsType<RpcException>(error.InnerException);
        Assert.Equal(ExtensionUri, Assert.Single(error.ActivatedExtensions));
        Assert.Empty(host.Handler.Error.ActivatedExtensions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtendedCard_IsDerivedOrPreservedWithoutMutatingTheHandlerCard(bool hasSignatures)
    {
        await using var host = await Host.StartAsync();
        if (hasSignatures)
        {
            host.Handler.AgentCardResult = host.Registry.PrepareAgentCard(host.Handler.AgentCardResult);
            Sign(host.Handler.AgentCardResult);
        }
        var before = Serialize(host.Handler.AgentCardResult);

        var card = await host.Client.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest());

        Assert.Equal(ExtensionUri, Assert.Single(card.Capabilities.Extensions!).Uri);
        Assert.Equal(before, Serialize(host.Handler.AgentCardResult));
        if (hasSignatures)
        {
            Assert.Equal("test-signature", Assert.Single(card.Signatures!).Signature);
            Assert.Equal("test-header", card.Signatures![0].Protected);
            host.Registry.ValidateAgentCard(card);
            // A protobuf bool uses false for an omitted value. It is not an optional field
            // and default values are omitted from the signing canonical form.
            Assert.False(card.Capabilities.Extensions![0].Required);
            Assert.Null(host.Handler.AgentCardResult.Capabilities.Extensions![0].Required);
        }
        else Assert.Null(host.Handler.AgentCardResult.Capabilities.Extensions);
    }

    [Fact]
    public async Task SignedExtendedCardMismatch_IsSanitizedWithoutLosingActivation()
    {
        await using var host = await Host.StartAsync();
        Sign(host.Handler.AgentCardResult);
        var before = Serialize(host.Handler.AgentCardResult);

        var error = await Assert.ThrowsAsync<A2AException>(() =>
            host.Client.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest()));

        Assert.Equal(A2AErrorCode.InternalError, error.ErrorCode);
        Assert.DoesNotContain("re-sign", error.Message, StringComparison.Ordinal);
        Assert.Equal(ExtensionUri, Assert.Single(error.ActivatedExtensions));
        Assert.Equal(before, Serialize(host.Handler.AgentCardResult));
    }

    [Fact]
    public async Task CardSupport_DoesNotDependOnTheRequestedActiveSet()
    {
        await using var host = await Host.StartAsync(requestExtension: false);

        var card = await host.Client.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest());

        Assert.Equal(ExtensionUri, Assert.Single(card.Capabilities.Extensions!).Uri);
    }

    [Fact]
    public async Task HandlerOnlyHosting_RemainsFreeOfExtensionCardValidation()
    {
        await using var host = await Host.StartAsync(negotiationEnabled: false);
        Sign(host.Handler.AgentCardResult);

        var card = await host.Client.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest());

        Assert.Null(card.Capabilities.Extensions);
        Assert.Equal("test-signature", Assert.Single(card.Signatures!).Signature);
    }

    private static void Sign(AgentCard card)
        => card.Signatures = [new AgentCardSignature { Protected = "test-header", Signature = "test-signature" }];

    private static string Serialize(AgentCard card) => JsonSerializer.Serialize(card, A2AJsonUtilities.DefaultOptions);

    private sealed class Host(WebApplication app, GrpcChannel channel, A2ACustomOperationRegistry registry, FakeRequestHandler handler) : IAsyncDisposable
    {
        public A2ACustomOperationRegistry Registry { get; } = registry;
        public FakeRequestHandler Handler { get; } = handler;
        public A2AGrpcClient Client { get; } = new(channel);

        public static async Task<Host> StartAsync(bool requestExtension = true, bool negotiationEnabled = true)
        {
            var registryBuilder = new A2ACustomOperationRegistryBuilder();
            registryBuilder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
            var registry = registryBuilder.Build();
            var handler = new FakeRequestHandler();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IA2ARequestHandler>(handler);
            if (negotiationEnabled) builder.Services.AddA2AGrpc(new A2AGrpcExtensionOptions(registry));
            else builder.Services.AddA2AGrpc();
            var app = builder.Build();
            app.MapGrpcA2A();
            await app.StartAsync();
            var server = app.GetTestServer();
            var transport = new ExtensionHeadersHandler(requestExtension) { InnerHandler = server.CreateHandler() };
            var channel = GrpcChannel.ForAddress(server.BaseAddress, new GrpcChannelOptions { HttpHandler = transport });
            return new Host(app, channel, registry, handler);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            channel.Dispose();
            await app.DisposeAsync();
        }
    }

    // A caller-owned transport can already add request metadata; no new mutable client-wide state
    // is introduced by the response-error API.
    private sealed class ExtensionHeadersHandler(bool requestExtension) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (requestExtension) request.Headers.Add("a2a-extensions", ExtensionUri);
            return base.SendAsync(request, cancellationToken);
        }
    }
}

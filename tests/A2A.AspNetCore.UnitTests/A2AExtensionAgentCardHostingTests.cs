using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Text;
using System.Text.Json;

namespace A2A.AspNetCore.Tests;

public sealed class A2AExtensionAgentCardHostingTests
{
    private const string ExtensionUri = "https://example.org/extensions/debate/v1";
    private const string HeaderName = "A2A-Extensions";

    [Fact]
    public async Task Discovery_DerivesADetachedCardAndDoesNotAdvertiseActivation()
    {
        var registry = CreateRegistry();
        var original = CreateCard();
        await using var app = CreateApp();
        app.MapWellKnownAgentCard(registry, original);
        original.Name = "changed later";
        original.Capabilities.Extensions = [new AgentExtension { Uri = "https://example.org/other" }];
        var context = CreateContext(app, "/.well-known/agent-card.json");
        context.Request.Headers[HeaderName] = ExtensionUri;

        await ExecuteAsync(app, context);
        var hosted = JsonSerializer.Deserialize<AgentCard>(ReadBody(context), A2AJsonUtilities.DefaultOptions)!;

        Assert.Equal("Debate agent", hosted.Name);
        Assert.Equal(ExtensionUri, Assert.Single(hosted.Capabilities.Extensions!).Uri);
        Assert.False(context.Response.Headers.ContainsKey(HeaderName));
        Assert.Null(context.Features.Get<A2ARequestContext>());
    }

    [Fact]
    public async Task Discovery_MatchingSignedCardPreservesTheSerializedContent()
    {
        var registry = CreateRegistry();
        var original = registry.PrepareAgentCard(CreateCard());
        Sign(original);
        var before = Serialize(original);
        await using var app = CreateApp();
        app.MapWellKnownAgentCard(registry, original);
        var context = CreateContext(app, "/.well-known/agent-card.json");

        await ExecuteAsync(app, context);

        Assert.Equal(before, ReadBody(context));
        Assert.Equal(before, Serialize(original));
    }

    [Fact]
    public async Task Discovery_SignedMismatchIsRejectedBeforeMapping()
    {
        var card = CreateCard();
        Sign(card);
        var before = Serialize(card);
        await using var app = CreateApp();

        var error = Assert.Throws<InvalidOperationException>(() => app.MapWellKnownAgentCard(CreateRegistry(), card));

        Assert.Contains("re-sign", error.Message, StringComparison.Ordinal);
        Assert.Empty(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints));
        Assert.Equal(before, Serialize(card));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task ExtendedCard_DerivesUnsignedAndPreservesMatchingSignedCards(bool jsonRpc, bool hasSignatures)
    {
        var registry = CreateRegistry();
        var card = hasSignatures ? registry.PrepareAgentCard(CreateCard()) : CreateCard();
        if (hasSignatures) Sign(card);
        var before = Serialize(card);
        var handler = new Mock<IA2ARequestHandler>();
        handler.Setup(h => h.GetExtendedAgentCardAsync(It.IsAny<GetExtendedAgentCardRequest>(), It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        await using var app = CreateApp();
        app.MapA2A(handler.Object, "/rpc", new A2AExtensionOptions(registry));
        app.MapHttpA2A(handler.Object, "/http", new A2AExtensionOptions(registry));
        var context = ExtendedContext(app, jsonRpc);
        context.Request.Headers[HeaderName] = ExtensionUri;

        await ExecuteAsync(app, context);
        using var document = JsonDocument.Parse(ReadBody(context));
        var payload = jsonRpc ? document.RootElement.GetProperty("result") : document.RootElement;
        var hosted = payload.Deserialize<AgentCard>(A2AJsonUtilities.DefaultOptions)!;

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(ExtensionUri, Assert.Single(hosted.Capabilities.Extensions!).Uri);
        Assert.Equal(ExtensionUri, context.Response.Headers[HeaderName].ToString());
        Assert.Equal(before, Serialize(card));
        if (hasSignatures) Assert.Equal(before, Serialize(hosted));
        else Assert.Null(card.Capabilities.Extensions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExtendedCard_SignedMismatchIsSanitizedButKeepsNegotiatedActivation(bool jsonRpc)
    {
        var card = CreateCard();
        Sign(card);
        var before = Serialize(card);
        var handler = new Mock<IA2ARequestHandler>();
        handler.Setup(h => h.GetExtendedAgentCardAsync(It.IsAny<GetExtendedAgentCardRequest>(), It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        await using var app = CreateApp();
        var options = new A2AExtensionOptions(CreateRegistry());
        app.MapA2A(handler.Object, "/rpc", options);
        app.MapHttpA2A(handler.Object, "/http", options);
        var context = ExtendedContext(app, jsonRpc);
        context.Request.Headers[HeaderName] = ExtensionUri;

        await ExecuteAsync(app, context);
        using var document = JsonDocument.Parse(ReadBody(context));
        var error = document.RootElement.GetProperty("error");

        if (jsonRpc) Assert.Equal((int)A2AErrorCode.InternalError, error.GetProperty("code").GetInt32());
        else Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.DoesNotContain("re-sign", ReadBody(context), StringComparison.Ordinal);
        Assert.Equal(ExtensionUri, context.Response.Headers[HeaderName].ToString());
        Assert.Equal(before, Serialize(card));
    }

    [Fact]
    public async Task EasyHosting_PreparesTheCardAndEnablesNegotiationForTheDiEndpoint()
    {
        var original = CreateCard();
        var registry = CreateRegistry();
        var calls = 0;
        var options = new A2AExtensionOptions(registry)
        {
            CanActivate = (_, _, _) => { calls++; return ValueTask.FromResult(true); },
        };
        var handler = new Mock<IA2ARequestHandler>();
        handler.Setup(h => h.GetTaskAsync(It.IsAny<GetTaskRequest>(), It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTask { Id = "task", ContextId = "context" });
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(handler.Object);
        builder.Services.AddA2AAgent<TestAgentHandler>(options, original);
        await using var app = builder.Build();
        app.MapA2A("/rpc");
        var context = CreateContext(app, "/rpc", "POST");
        SetRpcBody(context, A2AMethods.GetTask, "{\"id\":\"task\"}");
        context.Request.Headers[HeaderName] = ExtensionUri;

        await ExecuteAsync(app, context);

        Assert.Same(options, app.Services.GetRequiredService<A2AExtensionOptions>());
        Assert.Null(original.Capabilities.Extensions);
        Assert.Equal(ExtensionUri, Assert.Single(app.Services.GetRequiredService<AgentCard>().Capabilities.Extensions!).Uri);
        Assert.Equal(ExtensionUri, context.Response.Headers[HeaderName].ToString());
        Assert.Equal(1, calls);
        handler.Verify(h => h.GetTaskAsync(It.IsAny<GetTaskRequest>(), It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void EasyHosting_SignedMismatchDoesNotPartiallyRegisterServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var before = services.ToArray();
        var card = CreateCard();
        Sign(card);

        Assert.Throws<InvalidOperationException>(() =>
            services.AddA2AAgent<TestAgentHandler>(new A2AExtensionOptions(CreateRegistry()), card));

        Assert.Equal(before, services.ToArray());
        Assert.Single(card.Signatures!);
    }

    [Fact]
    public async Task LegacyExplicitNullCalls_RemainSourceCompatibleAndNegotiationFree()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddA2AAgent<TestAgentHandler>(CreateCard(), null);
        await using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<A2AExtensionOptions>());
        await using var app = CreateApp();
        // The invalid legacy null prefix is still resolved to the string overload, not
        // made ambiguous by the registry-aware overload.
        Assert.Throws<ArgumentNullException>(() => app.MapWellKnownAgentCard(CreateCard(), null!));
    }

    private static A2ACustomOperationRegistry CreateRegistry()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        return builder.Build();
    }

    private static AgentCard CreateCard() => new() { Name = "Debate agent", Description = "Discuss ideas", Version = "1.0" };

    private static void Sign(AgentCard card)
        => card.Signatures = [new AgentCardSignature { Protected = "test-header", Signature = "test-signature" }];

    private static string Serialize(AgentCard card) => JsonSerializer.Serialize(card, A2AJsonUtilities.DefaultOptions);

    private static WebApplication CreateApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Add(A2AJsonUtilities.DefaultOptions.TypeInfoResolver!));
        return builder.Build();
    }

    private static DefaultHttpContext CreateContext(WebApplication app, string path, string method = "GET")
    {
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static DefaultHttpContext ExtendedContext(WebApplication app, bool jsonRpc)
    {
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/extendedAgentCard", jsonRpc ? "POST" : "GET");
        if (jsonRpc) SetRpcBody(context, A2AMethods.GetExtendedAgentCard, "{}");
        return context;
    }

    private static void SetRpcBody(HttpContext context, string method, string parameters)
    {
        var bytes = Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"id\":\"card-1\",\"method\":\"{method}\",\"params\":{parameters}}}");
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bytes.Length;
    }

    private static Task ExecuteAsync(WebApplication app, HttpContext context)
        => ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == context.Request.Path.Value).RequestDelegate!(context);

    private static string ReadBody(HttpContext context) => Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    private sealed class TestAgentHandler : IAgentHandler
    {
        public Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

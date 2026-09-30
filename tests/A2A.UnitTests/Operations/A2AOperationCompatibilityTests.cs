using A2A.AspNetCore;
using A2A.UnitTests.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static A2A.UnitTests.Operations.A2AOperationDiagnosticsTests;

namespace A2A.UnitTests.Operations;

public class A2AOperationCompatibilityTests
{
    [Fact]
    public async Task ExistingEndpointAndClientFactoryOverloadsRemainCallable()
    {
        var handler = new A2AJsonRpcCustomOperationTests.TestRequestHandler();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IA2ARequestHandler>(handler);
        await using var app = builder.Build();
        var catalog = new A2AOperationCatalogBuilder().Build();
        var handlers = new A2AOperationHandlerCatalogBuilder().Build(catalog);
        A2ARequestScopeFactory factory = (_, _) => ValueTask.FromResult(new A2ARequestScope(new(handler)));
        Assert.IsAssignableFrom<IEndpointConventionBuilder>(app.MapA2A("/di"));
        Assert.IsAssignableFrom<IEndpointConventionBuilder>(app.MapA2A(handler, "/fixed"));
        Assert.IsAssignableFrom<IEndpointConventionBuilder>(app.MapA2A(factory, handlers,
            new A2AJsonRpcOperationBindingBuilder().Build(catalog), "/scoped"));
        Assert.IsAssignableFrom<IEndpointConventionBuilder>(app.MapHttpA2A(handler));
        Assert.IsAssignableFrom<IEndpointConventionBuilder>(app.MapHttpA2A(handler, "/fixed-http"));
        Assert.IsAssignableFrom<IEndpointConventionBuilder>(app.MapHttpA2A(factory, handlers,
            new A2AHttpOperationBindingBuilder().Build(), "/scoped-http"));

        var card = new AgentCard
        {
            Name = "compatibility",
            Description = "compatibility",
            Version = "1",
            SupportedInterfaces = [new AgentInterface { ProtocolBinding = "JSONRPC", Url = "http://localhost" }],
        };
        using var http = new HttpClient();
        var bindings = new A2AClientOperationBindingBuilder().Build();
        Assert.IsType<A2AClient>(A2AClientFactory.Create(card));
        Assert.IsType<A2AClient>(A2AClientFactory.Create(card, http));
        Assert.IsType<A2AClient>(A2AClientFactory.Create(card, http, new A2AClientOptions()));
        Assert.IsType<A2AClient>(A2AClientFactory.Create(card, bindings));
        Assert.IsType<A2AClient>(A2AClientFactory.Create(card, bindings, http));
        Assert.IsType<A2AClient>(A2AClientFactory.Create(card, bindings, http, new A2AClientOptions()));
    }

    [Fact]
    public async Task DuplicateAndForeignRegistrationsFailBeforeDispatch()
    {
        var builder = new A2AOperationCatalogBuilder();
        var operation = builder.DefineUnary<Request, Result>(new("test.unary"));
        Assert.Throws<InvalidOperationException>(() => builder.DefineUnary<Request, Request>(operation.Id));
        Assert.Throws<InvalidOperationException>(() => builder.DefineStreaming<Request, Result>(operation.Id));
        var catalog = builder.Build();
        Assert.Throws<InvalidOperationException>(() => catalog.GetRequired<Request, Request>(operation.Id));
        var foreign = new A2AOperation<Request, Result>(operation.Id);
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(operation, static (_, _, _) => ValueTask.FromResult(new Result()));
        Assert.Throws<InvalidOperationException>(() => handlers.Map(operation, static (_, _, _) => ValueTask.FromResult(new Result())));
        Assert.Throws<InvalidOperationException>(() => new A2AOperationHandlerCatalogBuilder()
            .Map(foreign, static (_, _, _) => ValueTask.FromResult(new Result())).Build(catalog));

        var rpc = new A2AJsonRpcOperationBindingBuilder()
            .Map("test/unary", operation, TestJsonContext.Default.Request, TestJsonContext.Default.Result);
        Assert.Throws<InvalidOperationException>(() =>
            rpc.Map("test/unary", operation, TestJsonContext.Default.Request, TestJsonContext.Default.Result));
        Assert.Throws<InvalidOperationException>(() => new A2AJsonRpcOperationBindingBuilder()
            .Map("test/unary", foreign, TestJsonContext.Default.Request, TestJsonContext.Default.Result).Build(catalog));

        var http = new A2AHttpOperationBindingBuilder()
            .Map("POST", "/test", operation, BindAsync, TestJsonContext.Default.Result);
        Assert.Throws<InvalidOperationException>(() => http.Map("POST", "/test", operation, BindAsync, TestJsonContext.Default.Result));
        await using var app = WebApplication.CreateBuilder().Build();
        A2ARequestScopeFactory factory = (_, _) => throw new InvalidOperationException("Must not dispatch");
        Assert.Throws<InvalidOperationException>(() => app.MapHttpA2A(factory, handlers.Build(catalog),
            new A2AHttpOperationBindingBuilder()
                .Map("POST", "/test", foreign, BindAsync, TestJsonContext.Default.Result).Build()));

        var client = new A2AClientOperationBindingBuilder()
            .MapJsonRpc(operation, "test/unary", TestJsonContext.Default.Request, TestJsonContext.Default.Result)
            .MapHttp(operation, MapAsync, TestJsonContext.Default.Result);
        Assert.Throws<InvalidOperationException>(() => client
            .MapJsonRpc(operation, "test/unary", TestJsonContext.Default.Request, TestJsonContext.Default.Result));
        Assert.Throws<InvalidOperationException>(() => client.MapHttp(operation, MapAsync, TestJsonContext.Default.Result));
        Assert.Throws<InvalidOperationException>(() => new A2AClientOperationBindingBuilder()
            .MapJsonRpc(foreign, "test/unary", TestJsonContext.Default.Request, TestJsonContext.Default.Result).Build(catalog));
        Assert.Throws<InvalidOperationException>(() => new A2AClientOperationBindingBuilder()
            .MapHttp(foreign, MapAsync, TestJsonContext.Default.Result).Build(catalog));
    }

    private static ValueTask<Request> BindAsync(HttpContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new Request("test"));

    private static ValueTask<HttpRequestMessage> MapAsync(Uri endpoint, Request request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new HttpRequestMessage(HttpMethod.Post, endpoint));
}

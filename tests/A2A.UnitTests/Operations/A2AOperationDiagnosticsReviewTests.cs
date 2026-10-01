using A2A.AspNetCore;
using A2A.UnitTests.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace A2A.UnitTests.Operations;

public partial class A2AOperationDiagnosticsTests
{
    [Theory]
    [InlineData("jsonrpc", false, false)]
    [InlineData("jsonrpc", true, false)]
    [InlineData("http-json", false, false)]
    [InlineData("http-json", true, false)]
    [InlineData("jsonrpc", false, true)]
    [InlineData("jsonrpc", true, true)]
    [InlineData("http-json", false, true)]
    [InlineData("http-json", true, true)]
    public async Task Server_ErrorMappingFailureDoesNotMutateUnownedActivities(
        string transport, bool streaming, bool sampleOperations)
    {
        using var capture = new ActivityCapture(sampleOperations);
        using var ambient = new Activity("caller").Start();
        using var cancellation = new CancellationTokenSource();
        var builder = new A2AOperationCatalogBuilder();
        var unary = builder.DefineUnary<Request, Result>(new("test.unary"));
        var stream = builder.DefineStreaming<Request, Result>(new("test.stream"));
        var unaryError = builder.DeclareError<Request, Result, Result>(unary, "test.error");
        var streamError = builder.DeclareError<Request, Result, Result>(stream, "test.error");
        var catalog = builder.Build();
        var details = new Result("serialization", cancellation);
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(unary, (_, _, _) => throw new A2AOperationException<Result>(unaryError, Secret, details))
            .MapStreaming(stream, (_, _, _) =>
                ThrowBeforeEventAsync(new A2AOperationException<Result>(streamError, Secret, details)))
            .Build(catalog);
        A2ARequestScopeFactory factory = (_, _) => ValueTask.FromResult(new A2ARequestScope(
            new A2AOperationContext(new A2AJsonRpcCustomOperationTests.TestRequestHandler())));
        await using var app = WebApplication.CreateBuilder().Build();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        using var body = new MemoryStream();
        context.Response.Body = body;
        if (transport == "jsonrpc")
        {
            var bindings = new A2AJsonRpcOperationBindingBuilder()
                .Map("test/unary", unary, TestJsonContext.Default.Request, TestJsonContext.Default.Result)
                .MapStreaming("test/stream", stream, TestJsonContext.Default.Request, TestJsonContext.Default.Result)
                .MapError(unary, unaryError, -32042, TestJsonContext.Default.Result)
                .MapError(stream, streamError, -32042, TestJsonContext.Default.Result)
                .Build(catalog);
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
                $$$"""{"jsonrpc":"2.0","id":42,"method":"test/{{{(streaming ? "stream" : "unary")}}}","params":{"value":"request"}}"""));
            var result = await A2AJsonRpcProcessor.ProcessRequestAsync(factory, handlers, bindings, context.Request, default);
            await result.ExecuteAsync(context);
        }
        else
        {
            var bindings = new A2AHttpOperationBindingBuilder()
                .Map("POST", "/unary", unary, BindAsync, TestJsonContext.Default.Result)
                .MapStreaming("POST", "/stream", stream, BindAsync, TestJsonContext.Default.Result)
                .MapError(unary, unaryError, 409, TestJsonContext.Default.Result)
                .MapError(stream, streamError, 409, TestJsonContext.Default.Result)
                .Build();
            app.MapHttpA2A(factory, handlers, bindings);
            context.Request.Method = "POST";
            var endpoint = ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
                .Single(endpoint => endpoint.RoutePattern.RawText == (streaming ? "/stream" : "/unary"));
            await endpoint.RequestDelegate!(context);
        }

        Assert.Equal(transport == "jsonrpc" ? 200 : 500, context.Response.StatusCode);
        using var response = JsonDocument.Parse(body.ToArray());
        Assert.Equal(transport == "jsonrpc" ? -32603 : 500,
            response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.DoesNotContain(Secret, Encoding.UTF8.GetString(body.ToArray()));
        Assert.Same(ambient, Activity.Current);
        Assert.Empty(ambient.TagObjects);
        Assert.Equal(ActivityStatusCode.Unset, ambient.Status);
        Assert.All(capture.Transports, diagnostic =>
            Assert.DoesNotContain(diagnostic.Tags.Keys, key => key.StartsWith("a2a.operation.", StringComparison.Ordinal)));
        if (sampleOperations)
        {
            AssertOperation(Assert.Single(capture.Operations), streaming ? "test.stream" : "test.unary",
                streaming, "extension", "server", transport, "error");
        }
        else
        {
            Assert.Empty(capture.Operations);
        }

        capture.AssertNoSecrets();

        static ValueTask<Request> BindAsync(HttpContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new Request("request"));
    }

    [Theory]
    [InlineData(-32001)]
    [InlineData(-32004)]
    public async Task StandardJsonRpc_PreStreamErrorPreservesRequestIdAndProtocolCode(int code)
    {
        using var capture = new ActivityCapture();
        var builder = new A2AOperationCatalogBuilder();
        var standard = builder.AddStandardA2AOperations();
        var catalog = builder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .MapStreaming(standard.SubscribeToTask, (_, request, _) =>
            {
                Assert.Equal("missing", request.Id);
                return ThrowStandardErrorAsync(new A2AException("Subscription rejected.", (A2AErrorCode)code));
            })
            .Build(catalog);
        var bindings = new A2AJsonRpcOperationBindingBuilder().AddStandardA2AJsonRpcBindings(standard).Build(catalog);
        A2ARequestScopeFactory factory = (_, _) => ValueTask.FromResult(new A2ARequestScope(
            new A2AOperationContext(new A2AJsonRpcCustomOperationTests.TestRequestHandler())));
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"jsonrpc":"2.0","id":"standard-id","method":"SubscribeToTask","params":{"id":"missing"}}"""));
        using var output = new MemoryStream();
        context.Response.Body = output;

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            factory,
            handlers,
            bindings,
            context.Request,
            default);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
        using (var outgoing = JsonDocument.Parse(output.ToArray()))
        {
            Assert.Equal(
                "2.0",
                outgoing.RootElement.GetProperty("jsonrpc").GetString());
            Assert.Equal(
                "standard-id",
                outgoing.RootElement.GetProperty("id").GetString());
            Assert.Equal(
                code,
                outgoing.RootElement.GetProperty("error")
                    .GetProperty("code").GetInt32());
        }

        AssertOperation(
            Assert.Single(capture.Operations),
            "https://a2a-protocol.org/operations/subscribe-to-task",
            true,
            "standard",
            "server",
            "jsonrpc",
            "error");
    }

    private static async IAsyncEnumerable<Result> ThrowBeforeEventAsync(Exception exception)
    {
        await Task.CompletedTask;
        throw exception;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<StreamResponse> ThrowStandardErrorAsync(Exception exception)
    {
        await Task.CompletedTask;
        throw exception;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}

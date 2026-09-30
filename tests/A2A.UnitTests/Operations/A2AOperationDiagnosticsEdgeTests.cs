using A2A.AspNetCore;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;

namespace A2A.UnitTests.Operations;

public partial class A2AOperationDiagnosticsTests
{
    [Theory]
    [InlineData("jsonrpc", false, "error")]
    [InlineData("jsonrpc", true, "cancelled")]
    [InlineData("http-json", false, "error")]
    [InlineData("http-json", true, "cancelled")]
    public async Task Client_DisposingBeforeStreamCompletionUsesEnumerationToken(
        string transport, bool cancel, string outcome)
    {
        using var capture = new ActivityCapture();
        using var callerCancellation = new CancellationTokenSource();
        using var enumerationCancellation = new CancellationTokenSource();
        var builder = new A2AOperationCatalogBuilder();
        var operation = builder.DefineStreaming<Request, Result>(new("test.stream"));
        var bindings = new A2AClientOperationBindingBuilder()
            .MapJsonRpcStreaming(operation, "test/stream", TestJsonContext.Default.Request, TestJsonContext.Default.Result)
            .MapHttpStreaming(operation, static (endpoint, _, _) =>
                ValueTask.FromResult(new HttpRequestMessage(HttpMethod.Post, endpoint)), TestJsonContext.Default.Result)
            .Build(builder.Build());
        using var httpClient = new HttpClient(new ResponseHandler("success", transport, true));
        IA2AClient client = transport == "jsonrpc"
            ? new A2AClient(new Uri("http://localhost"), bindings, httpClient)
            : new A2AHttpJsonClient(new Uri("http://localhost"), bindings, httpClient);
        await using (var enumerator = client.InvokeStreamingAsync(operation, new Request("request"), callerCancellation.Token)
            .GetAsyncEnumerator(enumerationCancellation.Token))
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(Secret, enumerator.Current.Value);
            Assert.Empty(capture.Operations);
            if (cancel)
            {
                await enumerationCancellation.CancelAsync();
            }
        }

        AssertOperation(Assert.Single(capture.Operations), "test.stream", true, "extension", "client", transport, outcome);
        capture.AssertNoSecrets();
    }

    [Fact]
    public async Task PublicStreamResult_DoesNotMutateAnAmbientOperation()
    {
        using var capture = new ActivityCapture();
        using var source = new ActivitySource("A2A");
        using var ambient = source.StartActivity("a2a.operation");
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;
        var task = new AgentTask { Id = "task", ContextId = "context", Status = new TaskStatus { State = TaskState.Working } };

        await new JsonRpcStreamedResult(StandardEventsAsync(task), new JsonRpcId(42)).ExecuteAsync(context);

        Assert.NotEmpty(body.ToArray());
        Assert.NotNull(ambient);
        Assert.Null(ambient.GetTagItem("a2a.operation.outcome"));
    }

    [Theory]
    [InlineData("jsonrpc", false, false)]
    [InlineData("jsonrpc", true, false)]
    [InlineData("http-json", false, false)]
    [InlineData("http-json", true, false)]
    [InlineData("jsonrpc", false, true)]
    [InlineData("jsonrpc", true, true)]
    [InlineData("http-json", false, true)]
    [InlineData("http-json", true, true)]
    public async Task Client_ErrorDetailDeserializationIsPrivateAndUncancelledFailuresAreErrors(
        string transport, bool streaming, bool throwsCancellation)
    {
        using var capture = new ActivityCapture();
        var builder = new A2AOperationCatalogBuilder();
        var unary = builder.DefineUnary<Request, Result>(new("test.unary"));
        var stream = builder.DefineStreaming<Request, Result>(new("test.stream"));
        var unaryError = builder.DeclareError<Request, Result, FaultingErrorDetails>(unary, "test.error");
        var streamError = builder.DeclareError<Request, Result, FaultingErrorDetails>(stream, "test.error");
        var bindings = new A2AClientOperationBindingBuilder()
            .MapJsonRpc(unary, "test/unary", TestJsonContext.Default.Request, TestJsonContext.Default.Result)
            .MapJsonRpcStreaming(stream, "test/stream", TestJsonContext.Default.Request, TestJsonContext.Default.Result)
            .MapJsonRpcError(unary, unaryError, -32042, EdgeJsonContext.Default.FaultingErrorDetails)
            .MapJsonRpcError(stream, streamError, -32042, EdgeJsonContext.Default.FaultingErrorDetails)
            .MapHttp(unary, MapAsync, TestJsonContext.Default.Result)
            .MapHttpStreaming(stream, MapAsync, TestJsonContext.Default.Result)
            .MapHttpError(unary, unaryError, 409, EdgeJsonContext.Default.FaultingErrorDetails)
            .MapHttpError(stream, streamError, 409, EdgeJsonContext.Default.FaultingErrorDetails)
            .Build(builder.Build());
        using var httpClient = new HttpClient(new ErrorDetailsHandler(transport, throwsCancellation));
        IA2AClient client = transport == "jsonrpc"
            ? new A2AClient(new Uri("http://localhost"), bindings, httpClient)
            : new A2AHttpJsonClient(new Uri("http://localhost"), bindings, httpClient);

        var exception = await Record.ExceptionAsync(async () =>
        {
            if (streaming)
            {
                await client.InvokeStreamingAsync(stream, new Request("request")).ToListAsync();
            }
            else
            {
                await client.InvokeAsync(unary, new Request("request"));
            }
        });

        if (throwsCancellation)
        {
            Assert.IsAssignableFrom<OperationCanceledException>(exception);
        }
        else
        {
            Assert.IsType<A2AException>(exception);
        }

        AssertOperation(Assert.Single(capture.Operations), streaming ? "test.stream" : "test.unary",
            streaming, "extension", "client", transport, "error");
        capture.AssertNoSecrets();

        static ValueTask<HttpRequestMessage> MapAsync(Uri endpoint, Request request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new HttpRequestMessage(HttpMethod.Post, endpoint));
    }

    [Theory]
    [InlineData("jsonrpc")]
    [InlineData("http-json")]
    public async Task Client_MalformedErrorDetailsDoNotMutateCallerWhenA2ASourceIsUnsampled(
        string transport)
    {
        using var listener = CreateUnsampledA2AListener();
        using var caller = new Activity("caller").Start();
        var builder = new A2AOperationCatalogBuilder();
        var operation = builder.DefineUnary<Request, Result>(new("test.unary"));
        var error = builder.DeclareError<Request, Result, FaultingErrorDetails>(
            operation,
            "test.error");
        var bindings = new A2AClientOperationBindingBuilder()
            .MapJsonRpc(
                operation,
                "test/unary",
                TestJsonContext.Default.Request,
                TestJsonContext.Default.Result)
            .MapJsonRpcError(
                operation,
                error,
                -32042,
                EdgeJsonContext.Default.FaultingErrorDetails)
            .MapHttp(
                operation,
                static (endpoint, _, _) =>
                    ValueTask.FromResult(
                        new HttpRequestMessage(HttpMethod.Post, endpoint)),
                TestJsonContext.Default.Result)
            .MapHttpError(
                operation,
                error,
                409,
                EdgeJsonContext.Default.FaultingErrorDetails)
            .Build(builder.Build());
        using var httpClient = new HttpClient(
            new ErrorDetailsHandler(transport, cancelled: false));
        IA2AClient client = transport == "jsonrpc"
            ? new A2AClient(new Uri("http://localhost"), bindings, httpClient)
            : new A2AHttpJsonClient(
                new Uri("http://localhost"),
                bindings,
                httpClient);

        await Assert.ThrowsAsync<A2AException>(
            () => client.InvokeAsync(operation, new Request("request")));

        Assert.Same(caller, Activity.Current);
        Assert.Equal(ActivityStatusCode.Unset, caller.Status);
        Assert.Empty(caller.TagObjects);
        Assert.Empty(caller.Events);
    }

    [Fact]
    public async Task HttpClient_MalformedErrorBodyDoesNotMutateCallerWhenA2ASourceIsUnsampled()
    {
        using var listener = CreateUnsampledA2AListener();
        using var caller = new Activity("caller").Start();
        var builder = new A2AOperationCatalogBuilder();
        var operation = builder.DefineUnary<Request, Result>(new("test.unary"));
        var bindings = new A2AClientOperationBindingBuilder()
            .MapHttp(
                operation,
                static (endpoint, _, _) =>
                    ValueTask.FromResult(
                        new HttpRequestMessage(HttpMethod.Post, endpoint)),
                TestJsonContext.Default.Result)
            .Build(builder.Build());
        using var httpClient = new HttpClient(new MalformedHttpErrorHandler());
        var client = new A2AHttpJsonClient(
            new Uri("http://localhost"),
            bindings,
            httpClient);

        await Assert.ThrowsAsync<A2AException>(
            () => client.InvokeAsync(operation, new Request("request")));

        Assert.Same(caller, Activity.Current);
        Assert.Equal(ActivityStatusCode.Unset, caller.Status);
        Assert.Empty(caller.TagObjects);
        Assert.Empty(caller.Events);
    }

    private static ActivityListener CreateUnsampledA2AListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "A2A",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.None,
            SampleUsingParentId =
                (ref ActivityCreationOptions<string> _) =>
                    ActivitySamplingResult.None,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class ErrorDetailsHandler(string transport, bool cancelled) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var reason = cancelled ? "cancel" : "error";
            return Task.FromResult(new HttpResponseMessage(transport == "jsonrpc" ? HttpStatusCode.OK : HttpStatusCode.Conflict)
            {
                Content = new StringContent((transport == "jsonrpc"
                    ? """{"jsonrpc":"2.0","id":42,"error":{"code":-32042,"message":"error","data":{"reason":"REASON"}}}"""
                    : """{"error":{"code":409,"message":"error","status":"CONFLICT","details":[{"@type":"test.error","reason":"REASON"}]}}""")
                    .Replace("REASON", reason, StringComparison.Ordinal),
                    Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class MalformedHttpErrorHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    "{malformed",
                    Encoding.UTF8,
                    "application/json"),
            });
    }

    internal sealed class FaultingErrorDetails
    {
        private string _reason = "";
        public string Reason
        {
            get => _reason;
            set
            {
                _reason = value;
                if (value == "cancel")
                {
                    throw new OperationCanceledException(Secret);
                }

                throw new InvalidOperationException(Secret);
            }
        }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(FaultingErrorDetails))]
    internal sealed partial class EdgeJsonContext : JsonSerializerContext;
}

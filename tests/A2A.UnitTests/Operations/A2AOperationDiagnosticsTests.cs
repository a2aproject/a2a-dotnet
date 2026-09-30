using A2A.AspNetCore;
using A2A.UnitTests.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.UnitTests.Operations;

[CollectionDefinition("OperationDiagnostics", DisableParallelization = true)]
public class OperationDiagnosticsGroup;

[Collection("OperationDiagnostics")]
public partial class A2AOperationDiagnosticsTests
{
    private const string Secret = "secret-payload-token-feature";

    public static TheoryData<string, bool, string> ServerCases
    {
        get
        {
            var cases = new TheoryData<string, bool, string>();
            foreach (var transport in new[] { "jsonrpc", "http-json" })
            {
                foreach (var streaming in new[] { false, true })
                {
                    foreach (var stage in new[]
                    {
                        "success", "binding", "validation", "factory", "handler",
                        "serialization", "write", "dispose", "cancel-factory",
                        "cancel-handler", "cancel-dispose",
                    })
                    {
                        cases.Add(transport, streaming, stage);
                    }

                    if (streaming)
                    {
                        foreach (var stage in new[] { "enumerator", "stream", "stream-dispose", "cancel-stream" })
                        {
                            cases.Add(transport, true, stage);
                        }
                    }
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(ServerCases))]
    public async Task Server_OneActivityCoversEntireInvocation(
        string transport, bool streaming, string stage)
    {
        using var capture = new ActivityCapture();
        var observed = new List<Activity?>();
        var builder = new A2AOperationCatalogBuilder();
        void Validate(Request request)
        {
            observed.Add(Activity.Current);
            Fail(stage, "validation");
        }

        var unary = builder.DefineUnary<Request, Result>(new("test.unary"), Validate);
        var stream = builder.DefineStreaming<Request, Result>(new("test.stream"), Validate);
        var catalog = builder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(unary, (_, _, _) =>
            {
                observed.Add(Activity.Current);
                Fail(stage, "handler");
                return ValueTask.FromResult(new Result(stage == "serialization"));
            })
            .MapStreaming(stream, (_, _, _) =>
            {
                observed.Add(Activity.Current);
                Fail(stage, "handler");
                return new Events(stage);
            })
            .Build(catalog);
        A2ARequestScopeFactory factory = (_, _) =>
        {
            observed.Add(Activity.Current);
            Fail(stage, "factory");
            var features = new A2AFeatureCollection();
            features.Set(new Request(Secret));
            return ValueTask.FromResult(new A2ARequestScope(
                new A2AOperationContext(new A2AJsonRpcCustomOperationTests.TestRequestHandler(), features),
                () =>
                {
                    observed.Add(Activity.Current);
                    Fail(stage, "dispose");
                    return ValueTask.CompletedTask;
                }));
        };
        await using var app = WebApplication.CreateBuilder().Build();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Headers.Authorization = $"Bearer {Secret}";
        context.Request.Headers["X-Extension-Token"] = Secret;
        context.Response.Body = stage == "write" ? new FailingStream() : new MemoryStream();
        context.Request.Method = "POST";
        var requestBody = stage == "binding" ? """{"value":{}}""" : $$"""{"value":"{{Secret}}"}""";

        async Task InvokeAsync()
        {
            if (transport == "jsonrpc")
            {
                var bindings = new A2AJsonRpcOperationBindingBuilder()
                    .Map("test/unary", unary, TestJsonContext.Default.Request, TestJsonContext.Default.Result)
                    .MapStreaming("test/stream", stream, TestJsonContext.Default.Request, TestJsonContext.Default.Result)
                    .Build(catalog);
                context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
                    $$"""{"jsonrpc":"2.0","id":42,"method":"test/{{(streaming ? "stream" : "unary")}}","params":{{requestBody}}}"""));
                var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
                    factory, handlers, bindings, context.Request, default);
                Assert.Empty(capture.Operations);
                await result.ExecuteAsync(context);
            }
            else
            {
                var bindings = new A2AHttpOperationBindingBuilder()
                    .Map("POST", "/unary", unary, BindAsync, TestJsonContext.Default.Result)
                    .MapStreaming("POST", "/stream", stream, BindAsync, TestJsonContext.Default.Result)
                    .Build();
                app.MapHttpA2A(factory, handlers, bindings);
                context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));
                var endpoint = ((IEndpointRouteBuilder)app).DataSources
                    .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
                    .Single(endpoint => endpoint.RoutePattern.RawText == (streaming ? "/stream" : "/unary"));
                await endpoint.RequestDelegate!(context);
            }
        }

        var exception = await Record.ExceptionAsync(InvokeAsync);
        if (stage == "success")
        {
            Assert.Null(exception);
            Assert.Equal(200, context.Response.StatusCode);
            Assert.Contains(Secret, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
        }

        var activity = Assert.Single(capture.Operations);
        AssertOperation(activity, streaming ? "test.stream" : "test.unary",
            streaming, "extension", "server", transport, Outcome(stage));
        Assert.All(observed, item => Assert.Same(activity, item));
        capture.AssertNoSecrets();

        async ValueTask<Request> BindAsync(HttpContext http, CancellationToken cancellationToken)
        {
            observed.Add(Activity.Current);
            return (await JsonSerializer.DeserializeAsync(
                http.Request.Body, TestJsonContext.Default.Request, cancellationToken))!;
        }
    }

    public static TheoryData<string, bool, string> ClientCases
    {
        get
        {
            var cases = new TheoryData<string, bool, string>();
            foreach (var transport in new[] { "jsonrpc", "http-json" })
            {
                foreach (var streaming in new[] { false, true })
                {
                    foreach (var stage in new[]
                    {
                        "success", "binding", "validation", "mapper", "response", "remote-error", "cancel-send", "secret-url",
                    })
                    {
                        cases.Add(transport, streaming, stage);
                    }

                    if (streaming)
                    {
                        cases.Add(transport, true, "pre-stream-error");
                    }
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(ClientCases))]
    public async Task Client_OneActivityCoversEntireInvocation(
        string transport, bool streaming, string stage)
    {
        using var capture = new ActivityCapture();
        var builder = new A2AOperationCatalogBuilder();
        var unary = builder.DefineUnary<Request, Result>(new("test.unary"), _ => Fail(stage, "validation"));
        var stream = builder.DefineStreaming<Request, Result>(new("test.stream"), _ => Fail(stage, "validation"));
        var catalog = builder.Build();
        var bindingBuilder = new A2AClientOperationBindingBuilder();
        if (stage != "binding")
        {
            bindingBuilder
                .MapJsonRpc(unary, "test/unary", TestJsonContext.Default.Request, TestJsonContext.Default.Result, Customize)
                .MapJsonRpcStreaming(stream, "test/stream", TestJsonContext.Default.Request, TestJsonContext.Default.Result, Customize)
                .MapHttp(unary, MapAsync, TestJsonContext.Default.Result)
                .MapHttpStreaming(stream, MapAsync, TestJsonContext.Default.Result);
        }

        using var httpClient = new HttpClient(new ResponseHandler(stage, transport, streaming));
        var endpoint = new Uri(stage == "secret-url" ? $"http://localhost/{Secret}?token={Secret}" : "http://localhost");
        IA2AClient client = transport == "jsonrpc"
            ? new A2AClient(endpoint, bindingBuilder.Build(catalog), httpClient)
            : new A2AHttpJsonClient(endpoint, bindingBuilder.Build(catalog), httpClient);
        List<Result>? results = null;
        var exception = await Record.ExceptionAsync(async () =>
        {
            if (streaming)
            {
                results = await client.InvokeStreamingAsync(stream, new Request(Secret)).ToListAsync();
            }
            else
            {
                results = [await client.InvokeAsync(unary, new Request(Secret))];
            }
        });
        if (stage is "success" or "secret-url")
        {
            Assert.Null(exception);
            Assert.Equal(Secret, Assert.Single(results!).Value);
        }
        else
        {
            Assert.NotNull(exception);
            if (stage == "pre-stream-error")
            {
                Assert.IsType<A2AException>(exception);
            }
        }

        AssertOperation(Assert.Single(capture.Operations), streaming ? "test.stream" : "test.unary",
            streaming, "extension", "client", transport, Outcome(stage));
        capture.AssertNoSecrets();

        void Customize(HttpRequestMessage message, Request request)
        {
            Fail(stage, "mapper");
            message.Headers.TryAddWithoutValidation("Authorization", $"Bearer {request.Value}");
        }

        ValueTask<HttpRequestMessage> MapAsync(Uri endpoint, Request request, CancellationToken cancellationToken)
        {
            Fail(stage, "mapper");
            return ValueTask.FromResult(new HttpRequestMessage(HttpMethod.Post,
                stage == "secret-url" ? new Uri(endpoint, $"?token={Secret}") : endpoint));
        }
    }

    [Theory]
    [InlineData("jsonrpc", false)]
    [InlineData("jsonrpc", true)]
    [InlineData("http-json", false)]
    [InlineData("http-json", true)]
    public async Task StandardClient_UsesStandardSource(string transport, bool streaming)
    {
        using var capture = new ActivityCapture();
        using var httpClient = new HttpClient(new ResponseHandler("remote-error", transport, streaming));
        IA2AClient client = transport == "jsonrpc"
            ? new A2AClient(new Uri("http://localhost"), httpClient)
            : new A2AHttpJsonClient(new Uri("http://localhost"), httpClient);

        if (streaming)
        {
            await Assert.ThrowsAsync<A2AException>(() =>
                client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "missing" }).ToListAsync().AsTask());
        }
        else
        {
            await Assert.ThrowsAsync<A2AException>(() => client.GetTaskAsync(new GetTaskRequest { Id = "missing" }));
        }

        AssertOperation(Assert.Single(capture.Operations),
            streaming ? "https://a2a-protocol.org/operations/subscribe-to-task" : "https://a2a-protocol.org/operations/get-task",
            streaming, "standard", "client", transport, "error");
    }

    [Theory]
    [InlineData("jsonrpc", false)]
    [InlineData("jsonrpc", true)]
    [InlineData("http-json", false)]
    [InlineData("http-json", true)]
    public async Task StandardServer_UsesStandardSourceAndPreservesWire(string transport, bool streaming)
    {
        using var capture = new ActivityCapture();
        var builder = new A2AOperationCatalogBuilder();
        var standard = builder.AddStandardA2AOperations();
        var catalog = builder.Build();
        var task = new AgentTask { Id = "task-1", ContextId = "context-1", Status = new TaskStatus { State = TaskState.Working } };
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(standard.GetTask, (_, _, _) => ValueTask.FromResult(task))
            .MapStreaming(standard.SubscribeToTask, (_, _, _) => StandardEventsAsync(task))
            .Build(catalog);
        A2ARequestScopeFactory factory = (_, _) => ValueTask.FromResult(new A2ARequestScope(
            new A2AOperationContext(new A2AJsonRpcCustomOperationTests.TestRequestHandler())));
        await using var app = WebApplication.CreateBuilder().Build();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        using var body = new MemoryStream();
        context.Response.Body = body;
        if (transport == "jsonrpc")
        {
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
                $$$"""{"jsonrpc":"2.0","id":"standard-id","method":"{{{(streaming ? "SubscribeToTask" : "GetTask")}}}","params":{"id":"task-1"}}"""));
            var result = await A2AJsonRpcProcessor.ProcessRequestAsync(factory, handlers,
                new A2AJsonRpcOperationBindingBuilder().AddStandardA2AJsonRpcBindings(standard).Build(catalog),
                context.Request, default);
            await result.ExecuteAsync(context);
        }
        else
        {
            app.MapHttpA2A(factory, handlers,
                new A2AHttpOperationBindingBuilder().AddStandardA2AHttpBindings(standard).Build());
            context.Request.RouteValues["id"] = "task-1";
            context.Request.Method = streaming ? "POST" : "GET";
            var endpoint = ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
                .Single(endpoint => endpoint.RoutePattern.RawText == (streaming ? "/tasks/{id}:subscribe" : "/tasks/{id}"));
            await endpoint.RequestDelegate!(context);
        }

        Assert.Equal(200, context.Response.StatusCode);
        var wire = Encoding.UTF8.GetString(body.ToArray());
        if (streaming)
        {
            Assert.StartsWith("data: ", wire);
            wire = wire["data: ".Length..].Trim();
        }

        using var document = JsonDocument.Parse(wire);
        var payload = document.RootElement;
        if (transport == "jsonrpc")
        {
            Assert.Equal("standard-id", payload.GetProperty("id").GetString());
            Assert.Equal("2.0", payload.GetProperty("jsonrpc").GetString());
            payload = payload.GetProperty("result");
        }

        if (streaming)
        {
            payload = payload.GetProperty("task");
        }

        Assert.Equal("task-1", payload.GetProperty("id").GetString());
        Assert.Equal("TASK_STATE_WORKING", payload.GetProperty("status").GetProperty("state").GetString());
        AssertOperation(Assert.Single(capture.Operations),
            streaming ? "https://a2a-protocol.org/operations/subscribe-to-task" : "https://a2a-protocol.org/operations/get-task",
            streaming, "standard", "server", transport, "success");
    }

    private static async IAsyncEnumerable<StreamResponse> StandardEventsAsync(AgentTask task)
    {
        await Task.CompletedTask;
        yield return new StreamResponse { Task = task };
    }

    private static string Outcome(string stage) =>
        stage is "success" or "secret-url" ? "success" : stage.StartsWith("cancel-", StringComparison.Ordinal) ? "cancelled" : "error";

    private static void Fail(string stage, string current)
    {
        if (stage == current)
        {
            throw new InvalidOperationException(Secret);
        }

        if (stage == $"cancel-{current}")
        {
            throw new OperationCanceledException(Secret);
        }
    }

    private static void AssertOperation(Activity activity, string id, bool streaming,
        string source, string role, string transport, string outcome)
    {
        Assert.Equal(id, activity.GetTagItem("a2a.operation.id"));
        Assert.Equal(streaming ? "streaming" : "unary", activity.GetTagItem("a2a.operation.kind"));
        Assert.Equal(source, activity.GetTagItem("a2a.operation.source"));
        Assert.Equal(role, activity.GetTagItem("a2a.operation.role"));
        Assert.Equal(transport, activity.GetTagItem("a2a.transport"));
        Assert.Equal(outcome, activity.GetTagItem("a2a.operation.outcome"));
        Assert.Equal(outcome == "error" ? ActivityStatusCode.Error : ActivityStatusCode.Unset, activity.Status);
        Assert.Equal(6, activity.TagObjects.Count());
    }

    private sealed class ActivityCapture : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly List<Activity> _activities = [];

        public ActivityCapture()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name is "A2A" or "A2A.AspNetCore",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity => _activities.Add(activity),
            };
            ActivitySource.AddActivityListener(_listener);
        }

        internal IEnumerable<Activity> Operations => _activities.Where(activity => activity.OperationName == "a2a.operation");

        internal void AssertNoSecrets()
        {
            foreach (var activity in _activities)
            {
                Assert.DoesNotContain(Secret, activity.StatusDescription ?? "", StringComparison.Ordinal);
                Assert.All(activity.TagObjects, tag => Assert.DoesNotContain(Secret, tag.Value?.ToString() ?? "", StringComparison.Ordinal));
                Assert.All(activity.Events.SelectMany(item => item.Tags),
                    tag => Assert.DoesNotContain(Secret, tag.Value?.ToString() ?? "", StringComparison.Ordinal));
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class Events(string stage) : IAsyncEnumerable<Result>, IAsyncEnumerator<Result>
    {
        private bool _moved;
        public Result Current => new(stage == "serialization");
        public IAsyncEnumerator<Result> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            Fail(stage, "enumerator");
            return this;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            Fail(stage, "stream");
            var next = !_moved;
            _moved = true;
            return ValueTask.FromResult(next);
        }

        public ValueTask DisposeAsync()
        {
            Fail(stage, "stream-dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new IOException(Secret);

        public override void Write(ReadOnlySpan<byte> buffer) =>
            throw new IOException(Secret);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException(Secret));

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException(new IOException(Secret));
    }

    private sealed class ResponseHandler(string stage, string transport, bool streaming) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Fail(stage, "send");
            var body = stage switch
            {
                "response" => """{"value":{}}""",
                "remote-error" or "pre-stream-error" when transport == "jsonrpc" => """{"jsonrpc":"2.0","id":42,"error":{"code":-32001,"message":"secret-payload-token-feature"}}""",
                "remote-error" or "pre-stream-error" => """{"error":{"code":404,"message":"secret-payload-token-feature","status":"NOT_FOUND"}}""",
                _ => $$"""{"value":"{{Secret}}"}""",
            };
            if (transport == "jsonrpc" && stage is not ("remote-error" or "pre-stream-error"))
            {
                body = $$"""{"jsonrpc":"2.0","id":42,"result":{{body}}}""";
            }

            var sse = streaming && stage != "pre-stream-error";
            return Task.FromResult(new HttpResponseMessage(
                stage is "remote-error" or "pre-stream-error" && transport == "http-json" ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new StringContent(sse ? $"data: {body}\n\n" : body,
                    Encoding.UTF8, sse ? "text/event-stream" : "application/json"),
            });
        }
    }

    internal sealed record Request(string Value);

    internal sealed class Result
    {
        private readonly bool _throws;
        private string _value = Secret;
        public Result() { }
        internal Result(bool throws) => _throws = throws;
        public string Value
        {
            get
            {
                if (_throws)
                {
                    throw new InvalidOperationException(Secret);
                }

                return _value;
            }
            set => _value = value;
        }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(Request))]
    [JsonSerializable(typeof(Result))]
    internal sealed partial class TestJsonContext : JsonSerializerContext;
}

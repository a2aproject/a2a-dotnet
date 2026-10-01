using A2A.AspNetCore;
using A2A.UnitTests.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Diagnostics;
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
                        "cancel-handler", "cancel-dispose", "cancel-serialization",
                        "uncancelled-factory", "uncancelled-handler", "uncancelled-dispose",
                        "uncancelled-serialization",
                    })
                    {
                        cases.Add(transport, streaming, stage);
                    }

                    if (streaming)
                    {
                        foreach (var stage in new[]
                        {
                            "enumerator", "stream", "later-stream", "stream-dispose", "cancel-stream",
                            "cancel-enumerator", "cancel-stream-dispose", "uncancelled-enumerator",
                            "uncancelled-stream", "uncancelled-stream-dispose",
                        })
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
        using var cancellation = new CancellationTokenSource();
        var observed = new List<Activity?>();
        var scopeCreated = false;
        var scopeDisposed = false;
        object? outcomeDuringScopeDisposal = null;
        var builder = new A2AOperationCatalogBuilder();
        void Validate(Request request)
        {
            observed.Add(Activity.Current);
            Fail(stage, "validation", cancellation);
        }

        var unary = builder.DefineUnary<Request, Result>(new("test.unary"), Validate);
        var stream = builder.DefineStreaming<Request, Result>(new("test.stream"), Validate);
        var catalog = builder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(unary, (_, _, _) =>
            {
                observed.Add(Activity.Current);
                Fail(stage, "handler", cancellation);
                return ValueTask.FromResult(new Result(stage, cancellation));
            })
            .MapStreaming(stream, (_, _, _) =>
            {
                observed.Add(Activity.Current);
                Fail(stage, "handler", cancellation);
                return new Events(stage, cancellation);
            })
            .Build(catalog);
        A2ARequestScopeFactory factory = (_, _) =>
        {
            observed.Add(Activity.Current);
            Fail(stage, "factory", cancellation);
            var features = new A2AFeatureCollection();
            features.Set(new Request(Secret));
            scopeCreated = true;
            return ValueTask.FromResult(new A2ARequestScope(
                new A2AOperationContext(new A2AJsonRpcCustomOperationTests.TestRequestHandler(), features),
                () =>
                {
                    observed.Add(Activity.Current);
                    scopeDisposed = true;
                    outcomeDuringScopeDisposal =
                        Activity.Current?.GetTagItem("a2a.operation.outcome");
                    Fail(stage, "dispose", cancellation);
                    return ValueTask.CompletedTask;
                }));
        };
        await using var app = WebApplication.CreateBuilder().Build();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.RequestAborted = cancellation.Token;
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
                    factory, handlers, bindings, context.Request, cancellation.Token);
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
        Assert.Equal(scopeCreated, scopeDisposed);
        if (streaming && stage is "success" or "dispose"
            or "cancel-dispose" or "uncancelled-dispose")
        {
            Assert.Null(outcomeDuringScopeDisposal);
        }

        AssertOperation(activity, streaming ? "test.stream" : "test.unary",
            streaming, "extension", "server", transport, Outcome(stage));
        Assert.All(observed, item => Assert.Same(activity, item));
        capture.AssertNoSecrets();
        if (transport == "jsonrpc" && stage is "validation" or "factory" or "handler"
            or "serialization" or "write" or "dispose" or "enumerator" or "stream" or "later-stream" or "stream-dispose")
        {
            var diagnostic = Assert.Single(capture.Transports);
            Assert.Equal("HandleA2ARequest", diagnostic.Name);
            Assert.Equal(ActivityStatusCode.Error, diagnostic.Status);
            Assert.Equal(stage == "write" ? "System.IO.IOException" : "System.InvalidOperationException",
                diagnostic.Tags.GetValueOrDefault("error.type"));
        }

        async ValueTask<Request> BindAsync(HttpContext http, CancellationToken cancellationToken)
        {
            observed.Add(Activity.Current);
            return (await JsonSerializer.DeserializeAsync(
                http.Request.Body, TestJsonContext.Default.Request, cancellationToken))!;
        }
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

    private static void Fail(string stage, string current, CancellationTokenSource? cancellation = null)
    {
        if (stage == current)
        {
            throw new InvalidOperationException(Secret);
        }

        if (stage == $"cancel-{current}")
        {
            Assert.NotNull(cancellation);
            cancellation.Cancel();
            throw new OperationCanceledException(Secret, cancellation.Token);
        }

        if (stage == $"uncancelled-{current}")
        {
            throw new OperationCanceledException(Secret, new CancellationToken(canceled: true));
        }

        if (stage == $"timeout-{current}")
        {
            throw new TaskCanceledException(Secret, new TimeoutException(Secret), new CancellationToken(canceled: true));
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
        internal List<(string Name, ActivityStatusCode Status, Dictionary<string, object?> Tags)> Transports { get; } = [];

        public ActivityCapture(bool sampleOperations = true)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name is "A2A" or "A2A.AspNetCore",
                Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                    !sampleOperations && options.Name == "a2a.operation"
                        ? ActivitySamplingResult.None : ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    _activities.Add(activity);
                    if (activity.OperationName != "a2a.operation")
                    {
                        Transports.Add((activity.OperationName, activity.Status, activity.TagObjects.ToDictionary()));
                    }
                },
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

    private sealed class Events(string stage, CancellationTokenSource cancellation) : IAsyncEnumerable<Result>, IAsyncEnumerator<Result>
    {
        private bool _moved;
        public Result Current => new(stage, cancellation);
        public IAsyncEnumerator<Result> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            Fail(stage, "enumerator", cancellation);
            return this;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            Fail(stage, "stream", cancellation);
            if (_moved)
            {
                Fail(stage, "later-stream", cancellation);
            }

            var next = !_moved;
            _moved = true;
            return ValueTask.FromResult(next);
        }

        public ValueTask DisposeAsync()
        {
            Fail(stage, "stream-dispose", cancellation);
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

    internal sealed record Request(string Value);

    internal sealed class Result
    {
        private readonly string _stage = "";
        private readonly CancellationTokenSource? _cancellation;
        private string _value = Secret;
        public Result() { }
        internal Result(string stage, CancellationTokenSource cancellation)
        {
            _stage = stage;
            _cancellation = cancellation;
        }
        public string Value
        {
            get
            {
                Fail(_stage, "serialization", _cancellation);
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

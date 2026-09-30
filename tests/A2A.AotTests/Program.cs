using A2A;
using A2A.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

if (JsonSerializer.IsReflectionEnabledByDefault)
{
    throw new InvalidOperationException("This validation requires reflection-free serialization.");
}

var builder = new A2AOperationCatalogBuilder();
var unary = builder.DefineUnary<ExtensionRequest, ExtensionResult>(new("aot.unary"));
var streaming = builder.DefineStreaming<ExtensionRequest, ExtensionEvent>(new("aot.streaming"));
var unaryError = builder.DeclareError<ExtensionRequest, ExtensionResult, ExtensionError>(unary, "aot.error");
var streamError = builder.DeclareError<ExtensionRequest, ExtensionEvent, ExtensionError>(streaming, "aot.error");
var catalog = builder.Build();
var json = ExtensionJsonContext.Default;
var handlers = new A2AOperationHandlerCatalogBuilder()
    .Map(unary, (_, request, _) =>
    {
        if (request.Value == "error")
        {
            throw new A2AOperationException<ExtensionError>(unaryError, "declared error", new("retry"));
        }

        return ValueTask.FromResult(new ExtensionResult(request.Value));
    })
    .MapStreaming(streaming, (_, request, cancellationToken) => EventsAsync(request, streamError, cancellationToken))
    .Build(catalog);
var rpcBindings = new A2AJsonRpcOperationBindingBuilder()
    .Map("aot/unary", unary, json.ExtensionRequest, json.ExtensionResult)
    .MapStreaming("aot/streaming", streaming, json.ExtensionRequest, json.ExtensionEvent)
    .MapError(unary, unaryError, -32042, json.ExtensionError)
    .MapError(streaming, streamError, -32042, json.ExtensionError)
    .Build(catalog);
var httpBindings = new A2AHttpOperationBindingBuilder()
    .Map("POST", "/unary", unary, BindAsync, json.ExtensionResult)
    .MapStreaming("POST", "/streaming", streaming, BindAsync, json.ExtensionEvent)
    .MapError(unary, unaryError, 409, json.ExtensionError)
    .MapError(streaming, streamError, 409, json.ExtensionError)
    .Build();
var clientBindings = new A2AClientOperationBindingBuilder()
    .MapJsonRpc(unary, "aot/unary", json.ExtensionRequest, json.ExtensionResult)
    .MapJsonRpcStreaming(streaming, "aot/streaming", json.ExtensionRequest, json.ExtensionEvent)
    .MapJsonRpcError(unary, unaryError, -32042, json.ExtensionError)
    .MapJsonRpcError(streaming, streamError, -32042, json.ExtensionError)
    .MapHttp(unary, (endpoint, request, _) => CreateRequestAsync(endpoint, "unary", request), json.ExtensionResult)
    .MapHttpStreaming(streaming, (endpoint, request, _) => CreateRequestAsync(endpoint, "streaming", request), json.ExtensionEvent)
    .MapHttpError(unary, unaryError, 409, json.ExtensionError)
    .MapHttpError(streaming, streamError, 409, json.ExtensionError)
    .Build(catalog);
var disposed = 0;
A2ARequestScopeFactory factory = (_, _) => ValueTask.FromResult(new A2ARequestScope(
    new A2AOperationContext(new UnusedHandler()),
    () =>
    {
        disposed++;
        return ValueTask.CompletedTask;
    }));
await using var app = WebApplication.CreateSlimBuilder().Build();
app.MapA2A(factory, handlers, rpcBindings, "/rpc");
app.MapHttpA2A(factory, handlers, httpBindings);
var endpoints = ((IEndpointRouteBuilder)app).DataSources
    .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
    .Where(endpoint => endpoint.RoutePattern.RawText is "/rpc" or "/unary" or "/streaming")
    .ToDictionary(endpoint => endpoint.RoutePattern.RawText!, endpoint => endpoint.RequestDelegate!);
using var httpClient = new HttpClient(new EndpointHandler(endpoints, app.Services));
foreach (var protocol in new[] { "jsonrpc", "http-json" })
{
    IA2AClient client = protocol == "jsonrpc"
        ? new A2AClient(new Uri("http://localhost/rpc"), clientBindings, httpClient)
        : new A2AHttpJsonClient(new Uri("http://localhost/"), clientBindings, httpClient);
    var response = await client.InvokeAsync(unary, new ExtensionRequest("request-result"));
    Require(response.Value == "request-result", "Unary request/result did not round-trip.");
    var events = await client.InvokeStreamingAsync(streaming, new ExtensionRequest("stream-event")).ToListAsync();
    Require(events.Count == 1 && events[0].Value == "stream-event", "Streaming event did not round-trip.");
    await ExpectErrorAsync(() => client.InvokeAsync(unary, new ExtensionRequest("error")));
    await ExpectErrorAsync(() => client.InvokeStreamingAsync(streaming, new ExtensionRequest("error")).ToListAsync().AsTask());
    Console.WriteLine($"{protocol}: unary, streaming, unary error, streaming error PASS");
}

Require(disposed == 8, "Each invocation must dispose its request scope.");
Console.WriteLine("Native AOT operation validation PASS (reflection disabled; 8 scopes disposed)");

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task ExpectErrorAsync(Func<Task> invoke)
{
    try
    {
        await invoke();
    }
    catch (A2AOperationException<ExtensionError> exception)
    {
        Require(exception.ErrorId == "aot.error" && exception.Details.Reason == "retry",
            "Declared error details did not round-trip.");
        return;
    }

    throw new InvalidOperationException("Expected a declared extension error.");
}

static async ValueTask<ExtensionRequest> BindAsync(HttpContext context, CancellationToken cancellationToken) =>
    (await JsonSerializer.DeserializeAsync(context.Request.Body, ExtensionJsonContext.Default.ExtensionRequest, cancellationToken))!;

static ValueTask<HttpRequestMessage> CreateRequestAsync(Uri endpoint, string path, ExtensionRequest request) =>
    ValueTask.FromResult(new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, path))
    {
        Content = JsonContent.Create(request, ExtensionJsonContext.Default.ExtensionRequest),
    });

static async IAsyncEnumerable<ExtensionEvent> EventsAsync(ExtensionRequest request,
    A2AOperationError<ExtensionError> error, [EnumeratorCancellation] CancellationToken cancellationToken)
{
    await Task.CompletedTask;
    cancellationToken.ThrowIfCancellationRequested();
    if (request.Value == "error")
    {
        throw new A2AOperationException<ExtensionError>(error, "declared error", new("retry"));
    }

    yield return new ExtensionEvent(request.Value);
}

internal sealed class EndpointHandler(
    IReadOnlyDictionary<string, RequestDelegate> endpoints, IServiceProvider services) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellationToken };
        context.Request.Method = request.Method.Method;
        foreach (var header in request.Headers)
        {
            context.Request.Headers[header.Key] = header.Value.ToArray();
        }

        context.Request.ContentType = "application/json";
        context.Request.Body = await request.Content!.ReadAsStreamAsync(cancellationToken);
        using var body = new MemoryStream();
        context.Response.Body = body;
        await endpoints[request.RequestUri!.AbsolutePath](context);
        var content = new ByteArrayContent(body.ToArray());
        content.Headers.ContentType = new(context.Response.ContentType!);
        return new HttpResponseMessage((HttpStatusCode)context.Response.StatusCode)
        {
            Content = content,
        };
    }
}

internal sealed record ExtensionRequest(string Value);
internal sealed record ExtensionResult(string Value);
internal sealed record ExtensionEvent(string Value);
internal sealed record ExtensionError(string Reason);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExtensionRequest))]
[JsonSerializable(typeof(ExtensionResult))]
[JsonSerializable(typeof(ExtensionEvent))]
[JsonSerializable(typeof(ExtensionError))]
internal sealed partial class ExtensionJsonContext : JsonSerializerContext;

internal sealed class UnusedHandler : IA2ARequestHandler
{
    public Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(TaskPushNotificationConfig config, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

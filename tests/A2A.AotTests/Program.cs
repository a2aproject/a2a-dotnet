using A2A;
using A2A.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

if (JsonSerializer.IsReflectionEnabledByDefault)
{
    throw new InvalidOperationException("This validation requires reflection-free serialization.");
}

var builder = new A2AOperationCatalogBuilder();
var standard = builder.AddStandardA2AOperations();
var unary = builder.DefineUnary<ExtensionRequest, ExtensionResult>(new("aot.unary"));
var streaming = builder.DefineStreaming<ExtensionRequest, ExtensionEvent>(new("aot.streaming"));
var unaryError = builder.DeclareError<ExtensionRequest, ExtensionResult, ExtensionError>(unary, "aot.error");
var streamError = builder.DeclareError<ExtensionRequest, ExtensionEvent, ExtensionError>(streaming, "aot.error");
var catalog = builder.Build();
var json = ExtensionJsonContext.Default;
var standardRequestTypeInfo =
    (JsonTypeInfo<SendMessageRequest>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(
        typeof(SendMessageRequest));
var handlers = new A2AOperationHandlerCatalogBuilder()
    .AddStandardA2AHandlers(standard)
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
    .AddStandardA2AJsonRpcBindings(standard)
    .Map("aot/unary", unary, json.ExtensionRequest, json.ExtensionResult)
    .MapStreaming("aot/streaming", streaming, json.ExtensionRequest, json.ExtensionEvent)
    .MapError(unary, unaryError, -32042, json.ExtensionError)
    .MapError(streaming, streamError, -32042, json.ExtensionError)
    .Build(catalog);
var httpBindings = new A2AHttpOperationBindingBuilder()
    .AddStandardA2AHttpBindings(standard)
    .Map("POST", "/unary", unary, BindAsync, json.ExtensionResult)
    .MapStreaming("POST", "/streaming", streaming, BindAsync, json.ExtensionEvent)
    .MapError(unary, unaryError, 409, json.ExtensionError)
    .MapError(streaming, streamError, 409, json.ExtensionError)
    .Build();
var disposed = 0;
var requestHandler = new AotRequestHandler();
A2ARequestScopeFactory factory = (_, _) => ValueTask.FromResult(new A2ARequestScope(
    new A2AOperationContext(requestHandler),
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
    .Where(endpoint => endpoint.RoutePattern.RawText is
        "/rpc" or
        "/unary" or
        "/streaming" or
        "/message:send" or
        "/message:stream")
    .ToDictionary(endpoint => endpoint.RoutePattern.RawText!, endpoint => endpoint.RequestDelegate!);

var rpcUnary = await InvokeJsonRpcAsync("aot/unary", "request-result");
Require(rpcUnary.Contains(
    "\"result\":{\"value\":\"request-result\"}",
    StringComparison.Ordinal),
    "JSON-RPC unary request/result did not round-trip.");
var rpcStream = await InvokeJsonRpcAsync("aot/streaming", "stream-event");
Require(rpcStream.Contains(
    "\"result\":{\"value\":\"stream-event\"}",
    StringComparison.Ordinal),
    "JSON-RPC streaming event did not round-trip.");
RequireDeclaredError(await InvokeJsonRpcAsync("aot/unary", "error"), -32042);
RequireDeclaredError(await InvokeJsonRpcAsync("aot/streaming", "error"), -32042);
Console.WriteLine("jsonrpc: unary, streaming, unary error, streaming error PASS");

var standardRpcUnary = await InvokeStandardJsonRpcAsync(A2AMethods.SendMessage);
Require(
    standardRpcUnary.Contains("\"messageId\":\"standard-unary\"", StringComparison.Ordinal),
    "JSON-RPC standard unary operation did not use IA2ARequestHandler.");
var standardRpcStream = await InvokeStandardJsonRpcAsync(A2AMethods.SendStreamingMessage);
Require(
    standardRpcStream.Contains("\"messageId\":\"standard-stream\"", StringComparison.Ordinal),
    "JSON-RPC standard streaming operation did not use IA2ARequestHandler.");

var httpUnary = await InvokeHttpAsync("/unary", "request-result");
Require(
    httpUnary.StatusCode == StatusCodes.Status200OK
    && httpUnary.Body.Contains(
        "\"value\":\"request-result\"",
        StringComparison.Ordinal),
    "HTTP unary request/result did not round-trip.");
var httpStream = await InvokeHttpAsync("/streaming", "stream-event");
Require(
    httpStream.StatusCode == StatusCodes.Status200OK
    && httpStream.Body.Contains(
        "\"value\":\"stream-event\"",
        StringComparison.Ordinal),
    "HTTP streaming event did not round-trip.");
var httpUnaryError = await InvokeHttpAsync("/unary", "error");
RequireDeclaredError(httpUnaryError.Body, 409);
var httpStreamError = await InvokeHttpAsync("/streaming", "error");
RequireDeclaredError(httpStreamError.Body, 409);
Console.WriteLine("http-json: unary, streaming, unary error, streaming error PASS");

var standardHttpUnary = await InvokeStandardHttpAsync("/message:send");
Require(
    standardHttpUnary.Body.Contains("\"messageId\":\"standard-unary\"", StringComparison.Ordinal),
    "HTTP standard unary operation did not use IA2ARequestHandler.");
var standardHttpStream = await InvokeStandardHttpAsync("/message:stream");
Require(
    standardHttpStream.Body.Contains("\"messageId\":\"standard-stream\"", StringComparison.Ordinal),
    "HTTP standard streaming operation did not use IA2ARequestHandler.");
Require(requestHandler.SendMessageCount == 2, "Standard unary handler must run for both transports.");
Require(requestHandler.StreamMessageCount == 2, "Standard streaming handler must run for both transports.");

Require(disposed == 12, "Each invocation must dispose its request scope.");
Console.WriteLine("Native AOT operation validation PASS (reflection disabled; 12 scopes disposed)");

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

void RequireDeclaredError(string body, int code)
{
    Require(
        body.Contains($"\"code\":{code}", StringComparison.Ordinal)
        && body.Contains("\"reason\":\"retry\"", StringComparison.Ordinal),
        "Declared error details did not round-trip.");
}

static async ValueTask<ExtensionRequest> BindAsync(HttpContext context, CancellationToken cancellationToken) =>
    (await JsonSerializer.DeserializeAsync(context.Request.Body, ExtensionJsonContext.Default.ExtensionRequest, cancellationToken))!;

async Task<string> InvokeJsonRpcAsync(string method, string value)
{
    var context = new DefaultHttpContext
    {
        RequestServices = app.Services,
    };
    context.Request.Method = HttpMethods.Post;
    context.Request.ContentType = "application/json";
    var parameters = JsonSerializer.Serialize(
        new ExtensionRequest(value),
        json.ExtensionRequest);
    context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
        $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"{method}\",\"params\":{parameters}}}"));
    using var responseBody = new MemoryStream();
    context.Response.Body = responseBody;
    await endpoints["/rpc"](context);
    return Encoding.UTF8.GetString(responseBody.ToArray());
}

async Task<string> InvokeStandardJsonRpcAsync(string method)
{
    var context = new DefaultHttpContext
    {
        RequestServices = app.Services,
    };
    context.Request.Method = HttpMethods.Post;
    context.Request.ContentType = "application/json";
    var parameters = JsonSerializer.Serialize(
        CreateStandardRequest(),
        standardRequestTypeInfo);
    context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
        $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"{method}\",\"params\":{parameters}}}"));
    using var responseBody = new MemoryStream();
    context.Response.Body = responseBody;
    await endpoints["/rpc"](context);
    return Encoding.UTF8.GetString(responseBody.ToArray());
}

async Task<(int StatusCode, string Body)> InvokeHttpAsync(
    string path,
    string value)
{
    var context = new DefaultHttpContext
    {
        RequestServices = app.Services,
    };
    context.Request.Method = HttpMethods.Post;
    context.Request.ContentType = "application/json";
    context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(
            new ExtensionRequest(value),
            json.ExtensionRequest)));
    using var responseBody = new MemoryStream();
    context.Response.Body = responseBody;
    await endpoints[path](context);
    return (
        context.Response.StatusCode,
        Encoding.UTF8.GetString(responseBody.ToArray()));
}

async Task<(int StatusCode, string Body)> InvokeStandardHttpAsync(string path)
{
    var context = new DefaultHttpContext
    {
        RequestServices = app.Services,
    };
    context.Request.Method = HttpMethods.Post;
    context.Request.ContentType = "application/json";
    context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(
            CreateStandardRequest(),
            standardRequestTypeInfo)));
    using var responseBody = new MemoryStream();
    context.Response.Body = responseBody;
    await endpoints[path](context);
    return (
        context.Response.StatusCode,
        Encoding.UTF8.GetString(responseBody.ToArray()));
}

static SendMessageRequest CreateStandardRequest() =>
    new()
    {
        Message = new Message
        {
            MessageId = "standard-request",
            Role = Role.User,
            Parts = [Part.FromText("request")],
        },
    };

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

internal sealed class AotRequestHandler : IA2ARequestHandler
{
    public int SendMessageCount { get; private set; }
    public int StreamMessageCount { get; private set; }

    public Task<SendMessageResponse> SendMessageAsync(
        SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        SendMessageCount++;
        return Task.FromResult(new SendMessageResponse
        {
            Message = CreateResponseMessage("standard-unary"),
        });
    }

    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(
        SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        StreamMessageCount++;
        return CreateStreamResponseAsync(cancellationToken);
    }

    public Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(TaskPushNotificationConfig config, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    private static Message CreateResponseMessage(string messageId) =>
        new()
        {
            MessageId = messageId,
            Role = Role.Agent,
            Parts = [Part.FromText("response")],
        };

    private static async IAsyncEnumerable<StreamResponse> CreateStreamResponseAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        yield return new StreamResponse
        {
            Message = CreateResponseMessage("standard-stream"),
        };
    }
}

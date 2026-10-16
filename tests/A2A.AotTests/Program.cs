using A2A;
using A2A.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

var registryBuilder = new A2ACustomOperationRegistryBuilder();
using var parameters = JsonDocument.Parse("{\"version\":1}");
var extension = registryBuilder.RegisterExtension(new AgentExtension
{
    Uri = "https://example.test/extensions/aot/v1",
    Description = "AOT custom operation registration",
    Required = false,
    Params = parameters.RootElement,
});
var unary = registryBuilder.Map<AotCustomRequest, AotCustomResult>(
    extension,
    new A2AOperationId("https://example.test/operations#aot-unary"),
    (request, _) => ValueTask.FromResult(new AotCustomResult(request.Value)),
    AotCustomJsonContext.Default.AotCustomRequest,
    AotCustomJsonContext.Default.AotCustomResult);
var streaming = registryBuilder.MapStreaming<AotCustomRequest, AotCustomResult>(
    extension,
    new A2AOperationId("https://example.test/operations#aot-stream"),
    StreamResults,
    AotCustomJsonContext.Default.AotCustomRequest,
    AotCustomJsonContext.Default.AotCustomResult);
var registry = registryBuilder.Build();
var inputCard = new AgentCard { Name = "AOT agent", Description = "Extension smoke tests", Version = "1.0" };
var hostedCard = registry.PrepareAgentCard(inputCard);
registry.ValidateAgentCard(hostedCard);
if (inputCard.Capabilities.Extensions is not null || hostedCard.Capabilities.Extensions?.Single().Uri != extension.Uri)
{
    throw new InvalidOperationException("Registry-consistent discovery changed the original card.");
}
hostedCard.Signatures = [new AgentCardSignature { Protected = "test-header", Signature = "test-signature" }];
var cardTypeInfo = (JsonTypeInfo<AgentCard>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(AgentCard));
var beforeSignedCard = JsonSerializer.Serialize(hostedCard, cardTypeInfo);
if (JsonSerializer.Serialize(registry.PrepareAgentCard(hostedCard), cardTypeInfo) != beforeSignedCard)
{
    throw new InvalidOperationException("Preparing a matching signed card changed its content.");
}
hostedCard.Capabilities.Extensions![0].Uri = "https://example.test/extensions/other/v1";
try
{
    registry.PrepareAgentCard(hostedCard);
    throw new InvalidOperationException("A mismatching signed card was accepted.");
}
catch (InvalidOperationException error) when (error.Message.Contains("re-sign", StringComparison.Ordinal))
{
    if (hostedCard.Signatures[0].Signature != "test-signature")
    {
        throw new InvalidOperationException("A mismatching signed card lost its signature.");
    }
}

using var errorHttp = new HttpClient(new AotErrorHandler(extension.Uri));
using var jsonRpcClient = new A2AClient(new Uri("https://example.test/rpc"), errorHttp);
using var restClient = new A2AHttpJsonClient(new Uri("https://example.test/http"), errorHttp);
foreach (IA2AClient client in new IA2AClient[] { jsonRpcClient, restClient })
{
    foreach (var isStreaming in new[] { false, true })
    {
        try
        {
            if (isStreaming)
            {
                await foreach (var _ in client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "missing" }).ConfigureAwait(false)) { }
            }
            else
            {
                await client.GetTaskAsync(new GetTaskRequest { Id = "missing" }).ConfigureAwait(false);
            }
            throw new InvalidOperationException("Expected the remote error to be surfaced.");
        }
        catch (A2AException error)
        {
            if (error.ErrorCode != A2AErrorCode.TaskNotFound || !error.ActivatedExtensions.Contains(extension.Uri))
            {
                throw new InvalidOperationException("Error code or activation metadata was lost.", error);
            }
        }
    }
}

var declaration = registry.Extensions[extension.Uri].ToAgentExtension();
var declarationJson = JsonSerializer.Serialize(declaration, AotCustomJsonContext.Default.AgentExtension);
if (!ReferenceEquals(unary.Extension, extension) || !ReferenceEquals(streaming.Extension, extension))
{
    throw new InvalidOperationException("Custom operation extension ownership was not preserved.");
}

var activation = await registry.NegotiateExtensionsAsync([extension.Uri], canActivate: static (_, cancellationToken) =>
{
    cancellationToken.ThrowIfCancellationRequested();
    return ValueTask.FromResult(true);
}).ConfigureAwait(false);
var inactive = await registry.NegotiateExtensionsAsync(
    [extension.Uri], canActivate: static (_, _) => ValueTask.FromResult(false)).ConfigureAwait(false);
if (inactive.ActivatedExtensions.Count != 0 || !activation.ActivatedExtensions.Contains(extension.Uri))
{
    throw new InvalidOperationException("Per-request activation decisions were not isolated.");
}

var operationContext = new A2ACustomOperationContext(features: [activation]);
var unaryResult = await registry.InvokeAsync(unary, operationContext, new AotCustomRequest("unary")).ConfigureAwait(false);
if (unaryResult.Value != "unary" || !activation.ActivatedExtensions.Contains(extension.Uri))
{
    throw new InvalidOperationException("Extension activation or unary dispatch failed.");
}

var streamEvents = 0;
await foreach (var streamEvent in registry.InvokeStreamingAsync(
    streaming, operationContext, new AotCustomRequest("stream")).ConfigureAwait(false))
{
    if (streamEvent.Value != "stream")
    {
        throw new InvalidOperationException("Extension streaming dispatch failed.");
    }

    streamEvents++;
}

if (streamEvents != 1)
{
    throw new InvalidOperationException("Expected one extension stream event.");
}

var jsonRpcBindings = new A2AJsonRpcCustomOperationBuilder()
    .Map("aot/unary", unary)
    .MapStreaming("aot/stream", streaming)
    .Build(registry);
var httpBindings = new A2AHttpCustomOperationBuilder()
    .Map(
        "POST",
        "/aot:unary",
        unary,
        (_, _) => ValueTask.FromResult(new AotCustomRequest("unary")))
    .MapStreaming(
        "GET",
        "/aot:stream",
        streaming,
        (_, _) => ValueTask.FromResult(new AotCustomRequest("stream")))
    .Build(registry);

var services = new ServiceCollection();
services.AddLogging();
services.AddA2AGrpcCustomOperations(registry);

var appBuilder = WebApplication.CreateSlimBuilder();
appBuilder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Add(A2AJsonUtilities.DefaultOptions.TypeInfoResolver!));
await using var app = appBuilder.Build();
await using var server = new A2AServer(
    new AotAgentHandler(), new InMemoryTaskStore(), new ChannelEventNotifier(), NullLogger<A2AServer>.Instance);
var extensionOptions = new A2AExtensionOptions(registry)
{
    CanActivate = static (_, _, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(true);
    },
};
app.MapA2A(server, "/rpc", extensionOptions, jsonRpcBindings);
app.MapHttpA2A(server, "/http", extensionOptions, httpBindings);
app.MapWellKnownAgentCard(registry, inputCard);
var endpoints = ((IEndpointRouteBuilder)app).DataSources
    .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
foreach (var (path, method, value) in new[]
{
    ("/rpc", "aot/unary", "unary"),
    ("/rpc", "aot/stream", "stream"),
    ("/http/aot:unary", "", "unary"),
    ("/http/aot:stream", "", "stream"),
})
{
    var httpContext = new DefaultHttpContext { RequestServices = app.Services };
    httpContext.Request.Method = path.EndsWith(":stream", StringComparison.Ordinal) ? HttpMethods.Get : HttpMethods.Post;
    httpContext.Request.Path = path;
    httpContext.Request.Headers["A2A-Extensions"] = extension.Uri;
    httpContext.Response.Body = new MemoryStream();
    if (path == "/rpc")
    {
        var body = Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"{method}\",\"params\":{{\"value\":\"{value}\"}}}}");
        httpContext.Request.Body = new MemoryStream(body);
        httpContext.Request.ContentLength = body.Length;
        httpContext.Request.ContentType = "application/json";
    }

    var endpoint = endpoints.Single(endpoint => endpoint.RoutePattern.RawText == path);
    await endpoint.RequestDelegate!(httpContext).ConfigureAwait(false);
    httpContext.Response.Body.Position = 0;
    var responseBody = await new StreamReader(httpContext.Response.Body).ReadToEndAsync().ConfigureAwait(false);
    if (httpContext.Response.StatusCode != StatusCodes.Status200OK ||
        httpContext.Response.Headers["A2A-Extensions"].ToString() != extension.Uri ||
        httpContext.Features.Get<A2ARequestContext>()?.ActivatedExtensions.Contains(extension.Uri) != true ||
        !responseBody.Contains($"\"value\":\"{value}\"", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Extension hosting or serialization failed for {path} {method}.");
    }
}

// Exercise the additive context-aware interface and adapter under Native AOT, including
// its default implementations for standard operations that do not execute agent code.
var contextualServer = server.WithRequestContext(activation);
var standardResponse = await contextualServer.SendMessageAsync(new SendMessageRequest
{
    Message = new Message { MessageId = "aot-standard", Role = Role.User, Parts = [Part.FromText("hello")] },
}).ConfigureAwait(false);
if (standardResponse.Message?.Parts[0].Text != "activation reached the agent")
    throw new InvalidOperationException("Native standard agent context propagation failed.");
try
{
    await contextualServer.GetTaskAsync(new GetTaskRequest { Id = "missing" }).ConfigureAwait(false);
    throw new InvalidOperationException("Expected the legacy standard operation error.");
}
catch (A2AException error) when (error.ErrorCode == A2AErrorCode.TaskNotFound) { }

var discoveryContext = new DefaultHttpContext { RequestServices = app.Services };
discoveryContext.Request.Method = HttpMethods.Get;
discoveryContext.Response.Body = new MemoryStream();
await endpoints.Single(endpoint => endpoint.RoutePattern.RawText == "/.well-known/agent-card.json")
    .RequestDelegate!(discoveryContext).ConfigureAwait(false);
var discoveredCard = JsonSerializer.Deserialize(
    ((MemoryStream)discoveryContext.Response.Body).ToArray(), cardTypeInfo)!;
if (discoveredCard.Name != inputCard.Name ||
    discoveredCard.Capabilities.Extensions?.Single().Uri != extension.Uri ||
    discoveryContext.Response.Headers.ContainsKey("A2A-Extensions"))
{
    throw new InvalidOperationException("Native discovery failed or confused support with activation.");
}

await VerifyHttpTenantSerializationAsync();

GC.KeepAlive(jsonRpcBindings);
GC.KeepAlive(httpBindings);
GC.KeepAlive(services);
GC.KeepAlive(declarationJson);
Console.WriteLine("Extension AOT smoke tests passed: registration, activation, HTTP hosting, error metadata and signed-card preservation.");

static async Task VerifyHttpTenantSerializationAsync()
{
    var request = new SendMessageRequest
    {
        Tenant = "top-level-tenant",
        Message = new Message
        {
            MessageId = "message-1",
            Role = Role.User,
            Parts = [Part.FromText("hi")],
        },
        Configuration = new SendMessageConfiguration
        {
            TaskPushNotificationConfig = new TaskPushNotificationConfig
            {
                Url = "https://push.example",
                Tenant = "embedded-tenant",
            },
        },
    };

    const string resultJson =
        """{"message":{"messageId":"response-1","role":"ROLE_AGENT","parts":[]}}""";
    var jsonRpcResponse = $$"""{"jsonrpc":"2.0","id":"1","result":{{resultJson}}}""";

    using var jsonRpcHttpClient = new HttpClient(new TenantSerializationHandler(jsonRpcResponse));
    using var jsonRpcClient = new A2AClient(new Uri("https://agent.example"), jsonRpcHttpClient);
    await jsonRpcClient.SendMessageAsync(request);

    using var httpJsonHttpClient = new HttpClient(new TenantSerializationHandler(resultJson));
    using var httpJsonClient = new A2AHttpJsonClient(new Uri("https://agent.example"), httpJsonHttpClient);
    await httpJsonClient.SendMessageAsync(request);
}

static async IAsyncEnumerable<AotCustomResult> StreamResults(
    AotCustomRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    await Task.Yield();
    yield return new AotCustomResult(request.Value);
}

internal sealed class TenantSerializationHandler(string responseBody) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        if (!body.Contains("\"tenant\":\"top-level-tenant\"", StringComparison.Ordinal) ||
            !body.Contains("\"tenant\":\"embedded-tenant\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("HTTP request did not serialize the expected tenant fields.");
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
    }
}

internal sealed record AotCustomRequest(string Value);

internal sealed class AotAgentHandler : IAgentHandler
{
    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        if (context.ExtensionActivation?.ActivatedExtensions.Contains("https://example.test/extensions/aot/v1") != true)
            throw new InvalidOperationException("The standard agent did not receive its activation snapshot.");
        await new MessageResponder(eventQueue, context.ContextId)
            .ReplyAsync("activation reached the agent", cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

internal sealed record AotCustomResult(string Value);

internal sealed class AotErrorHandler(string extensionUri) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isJsonRpc = request.RequestUri!.AbsolutePath == "/rpc";
        var response = new HttpResponseMessage(isJsonRpc ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.NotFound)
        {
            Content = new StringContent(isJsonRpc
                ? "{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"error\":{\"code\":-32001,\"message\":\"task not found\"}}"
                : "{\"error\":{\"code\":404,\"status\":\"NOT_FOUND\",\"message\":\"task not found\",\"details\":[{\"reason\":\"TASK_NOT_FOUND\",\"domain\":\"a2a-protocol.org\"}]}}",
                Encoding.UTF8, "application/json"),
        };
        response.Headers.Add("A2A-Extensions", extensionUri);
        return Task.FromResult(response);
    }
}

[JsonSerializable(typeof(AotCustomRequest))]
[JsonSerializable(typeof(AotCustomResult))]
[JsonSerializable(typeof(AgentExtension))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class AotCustomJsonContext : JsonSerializerContext;

using A2A;
using A2A.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var registryBuilder = new A2ACustomOperationRegistryBuilder();
var unary = registryBuilder.Map<AotCustomRequest, AotCustomResult>(
    new A2AOperationId("https://example.test/operations#aot-unary"),
    (request, _) => ValueTask.FromResult(new AotCustomResult(request.Value)),
    AotCustomJsonContext.Default.AotCustomRequest,
    AotCustomJsonContext.Default.AotCustomResult);
var streaming = registryBuilder.MapStreaming<AotCustomRequest, AotCustomResult>(
    new A2AOperationId("https://example.test/operations#aot-stream"),
    StreamResults,
    AotCustomJsonContext.Default.AotCustomRequest,
    AotCustomJsonContext.Default.AotCustomResult);
var registry = registryBuilder.Build();

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

await VerifyHttpTenantSerializationAsync();

GC.KeepAlive(jsonRpcBindings);
GC.KeepAlive(httpBindings);
GC.KeepAlive(services);

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

internal sealed record AotCustomResult(string Value);

[JsonSerializable(typeof(AotCustomRequest))]
[JsonSerializable(typeof(AotCustomResult))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class AotCustomJsonContext : JsonSerializerContext;

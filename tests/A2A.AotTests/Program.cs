using A2A;
using A2A.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
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

GC.KeepAlive(jsonRpcBindings);
GC.KeepAlive(httpBindings);
GC.KeepAlive(services);

static async IAsyncEnumerable<AotCustomResult> StreamResults(
    AotCustomRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    await Task.Yield();
    yield return new AotCustomResult(request.Value);
}

internal sealed record AotCustomRequest(string Value);

internal sealed record AotCustomResult(string Value);

[JsonSerializable(typeof(AotCustomRequest))]
[JsonSerializable(typeof(AotCustomResult))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class AotCustomJsonContext : JsonSerializerContext;

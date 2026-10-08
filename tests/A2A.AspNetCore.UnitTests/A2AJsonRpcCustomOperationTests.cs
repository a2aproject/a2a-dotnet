using Microsoft.AspNetCore.Http;
using Moq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.AspNetCore.Tests;

public sealed class A2AJsonRpcCustomOperationTests
{
    [Fact]
    public async Task ProcessRequestAsync_CustomUnaryMethod_InvokesTypedHandler()
    {
        HttpContext? handlerContext = null;
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<CustomRequest, CustomResult>(
            new A2AOperationId("https://example.test/operations#echo"),
            (context, request, _) =>
            {
                handlerContext = context.GetRequiredFeature<HttpContext>();
                return ValueTask.FromResult(new CustomResult(request.Value.ToUpperInvariant()));
            },
            CustomJsonContext.Default.CustomRequest,
            CustomJsonContext.Default.CustomResult);
        var registry = builder.Build();
        var bindings = new A2AJsonRpcCustomOperationBuilder()
            .Map("example/echo", operation)
            .Build(registry);
        var request = CreateRequest("""
            {
              "jsonrpc": "2.0",
              "id": "custom-1",
              "method": "example/echo",
              "params": { "value": "hello" }
            }
            """);

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            Mock.Of<IA2ARequestHandler>(),
            request,
            registry,
            bindings,
            CancellationToken.None);

        var response = await ExecuteJsonResultAsync(result);
        Assert.Equal("custom-1", response.Id.ToString());
        Assert.Equal("HELLO", response.Result!["value"]!.GetValue<string>());
        Assert.Same(request.HttpContext, handlerContext);
    }

    [Fact]
    public async Task ProcessRequestAsync_CustomUnaryMethod_InvalidParamsReturnsInvalidParams()
    {
        var (registry, bindings) = CreateUnaryBindings(
            (request, _) => ValueTask.FromResult(new CustomResult(request.Value)));
        var request = CreateRequest("""
            {
              "jsonrpc": "2.0",
              "id": 7,
              "method": "example/echo",
              "params": { "value": 42 }
            }
            """);

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            Mock.Of<IA2ARequestHandler>(),
            request,
            registry,
            bindings,
            CancellationToken.None);

        var response = await ExecuteJsonResultAsync(result);
        Assert.Equal("7", response.Id.ToString());
        Assert.Equal(A2AErrorCode.InvalidParams, (A2AErrorCode)response.Error!.Code);
    }

    [Fact]
    public async Task ProcessRequestAsync_CustomUnaryMethod_UnexpectedFailureIsSanitized()
    {
        var (registry, bindings) = CreateUnaryBindings(
            (_, _) => throw new InvalidOperationException("sensitive details"));
        var request = CreateRequest("""
            {
              "jsonrpc": "2.0",
              "id": "custom-error",
              "method": "example/echo",
              "params": { "value": "hello" }
            }
            """);

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            Mock.Of<IA2ARequestHandler>(),
            request,
            registry,
            bindings,
            CancellationToken.None);

        var response = await ExecuteJsonResultAsync(result);
        Assert.Equal(A2AErrorCode.InternalError, (A2AErrorCode)response.Error!.Code);
        Assert.DoesNotContain("sensitive", response.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessRequestAsync_CustomStreamingMethod_UsesExistingSseEnvelope()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.MapStreaming<CustomRequest, CustomResult>(
            new A2AOperationId("https://example.test/operations#watch"),
            StreamResults,
            CustomJsonContext.Default.CustomRequest,
            CustomJsonContext.Default.CustomResult);
        var registry = builder.Build();
        var bindings = new A2AJsonRpcCustomOperationBuilder()
            .MapStreaming("example/watch", operation)
            .Build(registry);
        var request = CreateRequest("""
            {
              "jsonrpc": "2.0",
              "id": "custom-stream",
              "method": "example/watch",
              "params": { "value": "event" }
            }
            """);

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            Mock.Of<IA2ARequestHandler>(),
            request,
            registry,
            bindings,
            CancellationToken.None);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal("text/event-stream", context.Response.ContentType);
        Assert.Contains("\"id\":\"custom-stream\"", body, StringComparison.Ordinal);
        Assert.Contains("\"value\":\"event-1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"value\":\"event-2\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_RejectsStandardMethodName()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<CustomRequest, CustomResult>(
            new A2AOperationId("https://example.test/operations#shadow"),
            (request, _) => ValueTask.FromResult(new CustomResult(request.Value)),
            CustomJsonContext.Default.CustomRequest,
            CustomJsonContext.Default.CustomResult);

        Assert.Throws<InvalidOperationException>(() =>
            new A2AJsonRpcCustomOperationBuilder().Map(A2AMethods.SendMessage, operation));
    }

    [Fact]
    public async Task ProcessRequestAsync_UnknownMethodStillReturnsMethodNotFound()
    {
        var registry = new A2ACustomOperationRegistryBuilder().Build();
        var bindings = new A2AJsonRpcCustomOperationBuilder().Build(registry);
        var request = CreateRequest("""
            {
              "jsonrpc": "2.0",
              "id": "unknown",
              "method": "example/unknown",
              "params": {}
            }
            """);

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            Mock.Of<IA2ARequestHandler>(),
            request,
            registry,
            bindings,
            CancellationToken.None);

        var response = await ExecuteJsonResultAsync(result);
        Assert.Equal(A2AErrorCode.MethodNotFound, (A2AErrorCode)response.Error!.Code);
    }

    private static (A2ACustomOperationRegistry Registry, A2AJsonRpcCustomOperationBindings Bindings)
        CreateUnaryBindings(A2ACustomOperationHandler<CustomRequest, CustomResult> handler)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map(
            new A2AOperationId("https://example.test/operations#echo"),
            handler,
            CustomJsonContext.Default.CustomRequest,
            CustomJsonContext.Default.CustomResult);
        var registry = builder.Build();
        var bindings = new A2AJsonRpcCustomOperationBuilder()
            .Map("example/echo", operation)
            .Build(registry);
        return (registry, bindings);
    }

    private static HttpRequest CreateRequest(string json)
    {
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Request.ContentType = "application/json";
        return context.Request;
    }

    private static async Task<JsonRpcResponse> ExecuteJsonResultAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return (await JsonSerializer.DeserializeAsync<JsonRpcResponse>(
            context.Response.Body,
            A2AJsonUtilities.DefaultOptions))!;
    }

    private static async IAsyncEnumerable<CustomResult> StreamResults(
        CustomRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new CustomResult($"{request.Value}-1");
        await Task.Yield();
        yield return new CustomResult($"{request.Value}-2");
    }
}

internal sealed record CustomRequest(string Value);

internal sealed record CustomResult(string Value);

[JsonSerializable(typeof(CustomRequest))]
[JsonSerializable(typeof(CustomResult))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class CustomJsonContext : JsonSerializerContext;

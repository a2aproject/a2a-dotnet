using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.AspNetCore.Tests;

public sealed class A2AHttpCustomOperationTests
{
    [Fact]
    public async Task MapHttpA2A_CustomUnaryRoute_BindsAndInvokesTypedHandler()
    {
        HttpContext? handlerContext = null;
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<HttpCustomRequest, HttpCustomResult>(
            new A2AOperationId("https://example.test/operations#resume"),
            (context, request, _) =>
            {
                handlerContext = context.GetRequiredFeature<HttpContext>();
                return ValueTask.FromResult(
                    new HttpCustomResult($"{request.TaskId}:{request.Token}:{request.Value}"));
            },
            HttpCustomJsonContext.Default.HttpCustomRequest,
            HttpCustomJsonContext.Default.HttpCustomResult);
        var registry = builder.Build();
        var bindings = new A2AHttpCustomOperationBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{taskId}:resume",
                operation,
                async (context, cancellationToken) =>
                {
                    var body = await JsonSerializer.DeserializeAsync(
                        context.Request.Body,
                        HttpCustomJsonContext.Default.HttpCustomBody,
                        cancellationToken);
                    return new HttpCustomRequest(
                        context.Request.RouteValues["taskId"]!.ToString()!,
                        context.Request.Headers["x-auth-token"].ToString(),
                        body!.Value);
                })
            .Build(registry);
        await using var app = CreateApp();
        app.MapHttpA2A(Mock.Of<IA2ARequestHandler>(), "/a2a", registry, bindings);
        var endpoint = FindEndpoint(app, "/a2a/tasks/{taskId}:resume", HttpMethods.Post);
        var context = CreateContext(app, HttpMethods.Post, "/a2a/tasks/task-1:resume");
        context.Request.RouteValues["taskId"] = "task-1";
        context.Request.Headers["x-auth-token"] = "token-1";
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new HttpCustomBody("payload"),
            HttpCustomJsonContext.Default.HttpCustomBody);
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Request.ContentType = "application/json";
        context.Features.Set(Mock.Of<IHttpRequestBodyDetectionFeature>(
            feature => feature.CanHaveBody));

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync(
            context.Response.Body,
            HttpCustomJsonContext.Default.HttpCustomResult);
        Assert.Equal("task-1:token-1:payload", response!.Value);
        Assert.Same(context, handlerContext);
    }

    [Fact]
    public async Task MapHttpA2A_CustomStreamingRoute_WritesSseEvents()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.MapStreaming<HttpCustomRequest, HttpCustomResult>(
            new A2AOperationId("https://example.test/operations#watch"),
            StreamResults,
            HttpCustomJsonContext.Default.HttpCustomRequest,
            HttpCustomJsonContext.Default.HttpCustomResult);
        var registry = builder.Build();
        var bindings = new A2AHttpCustomOperationBuilder()
            .MapStreaming(
                HttpMethods.Get,
                "/authorization/{taskId}:watch",
                operation,
                (context, _) => ValueTask.FromResult(
                    new HttpCustomRequest(
                        context.Request.RouteValues["taskId"]!.ToString()!,
                        "",
                        "event")))
            .Build(registry);
        await using var app = CreateApp();
        app.MapHttpA2A(Mock.Of<IA2ARequestHandler>(), "/a2a", registry, bindings);
        var endpoint = FindEndpoint(app, "/a2a/authorization/{taskId}:watch", HttpMethods.Get);
        var context = CreateContext(app, HttpMethods.Get, "/a2a/authorization/task-2:watch");
        context.Request.RouteValues["taskId"] = "task-2";
        var responseBodyFeature = new Mock<IHttpResponseBodyFeature>();
        responseBodyFeature.SetupGet(feature => feature.Stream).Returns(context.Response.Body);
        context.Features.Set(responseBodyFeature.Object);

        await endpoint.RequestDelegate!(context);

        Assert.Equal("text/event-stream", context.Response.ContentType);
        Assert.Equal("no-cache,no-store", context.Response.Headers.CacheControl);
        Assert.Equal("no-cache", context.Response.Headers.Pragma);
        Assert.Equal("identity", context.Response.Headers.ContentEncoding);
        responseBodyFeature.Verify(feature => feature.DisableBuffering(), Times.Once);
        context.Response.Body.Position = 0;
        var response = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("\"value\":\"event-1\"", response, StringComparison.Ordinal);
        Assert.Contains("\"value\":\"event-2\"", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapHttpA2A_CustomUnaryRoute_MapsA2AException()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<HttpCustomRequest, HttpCustomResult>(
            new A2AOperationId("https://example.test/operations#error"),
            (_, _) => throw new A2AException("invalid authorization", A2AErrorCode.InvalidParams),
            HttpCustomJsonContext.Default.HttpCustomRequest,
            HttpCustomJsonContext.Default.HttpCustomResult);
        var registry = builder.Build();
        var bindings = new A2AHttpCustomOperationBuilder()
            .Map(
                HttpMethods.Post,
                "/error",
                operation,
                (_, _) => ValueTask.FromResult(new HttpCustomRequest("", "", "")))
            .Build(registry);
        await using var app = CreateApp();
        app.MapHttpA2A(Mock.Of<IA2ARequestHandler>(), "/a2a", registry, bindings);
        var endpoint = FindEndpoint(app, "/a2a/error", HttpMethods.Post);
        var context = CreateContext(app, HttpMethods.Post, "/a2a/error");

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var response = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("invalid authorization", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapHttpA2A_CustomUnaryRoute_MapsMalformedJsonBodyToInvalidParams()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<HttpCustomBody, HttpCustomResult>(
            new A2AOperationId("https://example.test/operations#malformed-json"),
            (request, _) => ValueTask.FromResult(new HttpCustomResult(request.Value)),
            HttpCustomJsonContext.Default.HttpCustomBody,
            HttpCustomJsonContext.Default.HttpCustomResult);
        var registry = builder.Build();
        var bindings = new A2AHttpCustomOperationBuilder()
            .Map(
                HttpMethods.Post,
                "/malformed-json",
                operation,
                async (context, cancellationToken) =>
                    (await JsonSerializer.DeserializeAsync(
                        context.Request.Body,
                        HttpCustomJsonContext.Default.HttpCustomBody,
                        cancellationToken))!)
            .Build(registry);
        await using var app = CreateApp();
        app.MapHttpA2A(Mock.Of<IA2ARequestHandler>(), "/a2a", registry, bindings);
        var endpoint = FindEndpoint(app, "/a2a/malformed-json", HttpMethods.Post);
        var context = CreateContext(app, HttpMethods.Post, "/a2a/malformed-json");
        context.Request.Body = new MemoryStream("{"u8.ToArray());

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("application/a2a+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        var response = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("The custom operation request body is invalid.", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapHttpA2A_CustomUnaryRoute_RejectsUnsupportedVersion()
    {
        var handlerInvoked = false;
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<HttpCustomRequest, HttpCustomResult>(
            new A2AOperationId("https://example.test/operations#version"),
            (request, _) =>
            {
                handlerInvoked = true;
                return ValueTask.FromResult(new HttpCustomResult(request.Value));
            },
            HttpCustomJsonContext.Default.HttpCustomRequest,
            HttpCustomJsonContext.Default.HttpCustomResult);
        var registry = builder.Build();
        var bindings = new A2AHttpCustomOperationBuilder()
            .Map(HttpMethods.Post, "/version", operation, BindEmpty)
            .Build(registry);
        await using var app = CreateApp();
        app.MapHttpA2A(Mock.Of<IA2ARequestHandler>(), "/a2a", registry, bindings);
        var endpoint = FindEndpoint(app, "/a2a/version", HttpMethods.Post);
        var context = CreateContext(app, HttpMethods.Post, "/a2a/version");
        context.Request.Headers["A2A-Version"] = "99.0";

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False(handlerInvoked);
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(
            "VERSION_NOT_SUPPORTED",
            response.RootElement.GetProperty("error").GetProperty("details")[0].GetProperty("reason").GetString());
    }

    [Fact]
    public void Map_RejectsDuplicateMethodAndRoute()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<HttpCustomRequest, HttpCustomResult>(
            new A2AOperationId("https://example.test/operations#duplicate-http"),
            (request, _) => ValueTask.FromResult(new HttpCustomResult(request.Value)),
            HttpCustomJsonContext.Default.HttpCustomRequest,
            HttpCustomJsonContext.Default.HttpCustomResult);
        var bindings = new A2AHttpCustomOperationBuilder()
            .Map(HttpMethods.Post, "/duplicate", operation, BindEmpty);

        Assert.Throws<InvalidOperationException>(() =>
            bindings.Map("post", "/duplicate", operation, BindEmpty));
    }

    [Fact]
    public void Map_RejectsStandardRouteShadowing()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<HttpCustomRequest, HttpCustomResult>(
            new A2AOperationId("https://example.test/operations#shadow-http"),
            (request, _) => ValueTask.FromResult(new HttpCustomResult(request.Value)),
            HttpCustomJsonContext.Default.HttpCustomRequest,
            HttpCustomJsonContext.Default.HttpCustomResult);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new A2AHttpCustomOperationBuilder()
                .Map(HttpMethods.Post, "/message:send", operation, BindEmpty));

        Assert.Contains("cannot replace", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("post", "/MESSAGE:SEND")]
    [InlineData("get", "/tasks/{taskId}")]
    public void Map_RejectsEquivalentStandardRouteShadowing(string method, string route)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<HttpCustomRequest, HttpCustomResult>(
            new A2AOperationId("https://example.test/operations#equivalent-shadow-http"),
            (request, _) => ValueTask.FromResult(new HttpCustomResult(request.Value)),
            HttpCustomJsonContext.Default.HttpCustomRequest,
            HttpCustomJsonContext.Default.HttpCustomResult);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new A2AHttpCustomOperationBuilder()
                .Map(method, route, operation, BindEmpty));

        Assert.Contains("cannot replace", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_RejectsEquivalentCustomRoute()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<HttpCustomRequest, HttpCustomResult>(
            new A2AOperationId("https://example.test/operations#equivalent-duplicate-http"),
            (request, _) => ValueTask.FromResult(new HttpCustomResult(request.Value)),
            HttpCustomJsonContext.Default.HttpCustomRequest,
            HttpCustomJsonContext.Default.HttpCustomResult);
        var bindings = new A2AHttpCustomOperationBuilder()
            .Map(HttpMethods.Post, "/tasks/{taskId}:resume", operation, BindEmpty);

        Assert.Throws<InvalidOperationException>(() =>
            bindings.Map("post", "/TASKS/{id}:resume", operation, BindEmpty));
    }

    [Fact]
    public async Task MapHttpA2A_CustomUnaryRoute_SanitizesUnexpectedFailures()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<HttpCustomRequest, HttpCustomResult>(
            new A2AOperationId("https://example.test/operations#failure"),
            (_, _) => throw new InvalidOperationException("sensitive detail"),
            HttpCustomJsonContext.Default.HttpCustomRequest,
            HttpCustomJsonContext.Default.HttpCustomResult);
        var registry = builder.Build();
        var bindings = new A2AHttpCustomOperationBuilder()
            .Map(
                HttpMethods.Post,
                "/failure",
                operation,
                (_, _) => ValueTask.FromResult(new HttpCustomRequest("", "", "")))
            .Build(registry);
        await using var app = CreateApp();
        app.MapHttpA2A(Mock.Of<IA2ARequestHandler>(), "/a2a", registry, bindings);
        var endpoint = FindEndpoint(app, "/a2a/failure", HttpMethods.Post);
        var context = CreateContext(app, HttpMethods.Post, "/a2a/failure");

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var response = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("An internal error occurred.", response, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive detail", response, StringComparison.Ordinal);
    }

    private static ValueTask<HttpCustomRequest> BindEmpty(
        HttpContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new HttpCustomRequest("", "", ""));

    private static WebApplication CreateApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Add(
                A2AJsonUtilities.DefaultOptions.TypeInfoResolver!));
        return builder.Build();
    }

    private static RouteEndpoint FindEndpoint(
        WebApplication app,
        string route,
        string method) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint =>
                endpoint.RoutePattern.RawText == route &&
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method));

    private static DefaultHttpContext CreateContext(
        WebApplication app,
        string method,
        string path)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async IAsyncEnumerable<HttpCustomResult> StreamResults(
        HttpCustomRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new HttpCustomResult($"{request.Value}-1");
        await Task.Yield();
        yield return new HttpCustomResult($"{request.Value}-2");
    }
}

internal sealed record HttpCustomRequest(string TaskId, string Token, string Value);

internal sealed record HttpCustomBody(string Value);

internal sealed record HttpCustomResult(string Value);

[JsonSerializable(typeof(HttpCustomRequest))]
[JsonSerializable(typeof(HttpCustomBody))]
[JsonSerializable(typeof(HttpCustomResult))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class HttpCustomJsonContext : JsonSerializerContext;

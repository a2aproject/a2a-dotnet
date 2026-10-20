using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Moq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace A2A.AspNetCore.Tests;

public sealed class A2AExtensionHostingTests
{
    private const string ExtensionUri = "https://example.org/extensions/debate/v1";
    private const string OtherUri = "https://example.org/extensions/usage/v1";
    private const string HeaderName = "A2A-Extensions";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UsageExample_ActivatesOnceAndInvokesTheMappedOperation(bool jsonRpc)
    {
        A2ARequestContext? seen = null;
        var builder = new A2ACustomOperationRegistryBuilder();
        var debate = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var resume = builder.Map<CustomRequest, CustomResult>(
            debate, new A2AOperationId("https://example.org/operations#resume"),
            (context, request, _) =>
            {
                seen = context.GetRequiredFeature<A2ARequestContext>();
                Assert.Same(seen, context.ExtensionActivation);
                Assert.True(context.TryGetFeature<HttpContext>(out var http));
                Assert.Same(context.GetRequiredFeature<HttpContext>(), http);
                Assert.Same(seen, context.GetRequiredFeature<HttpContext>().Features.Get<A2ARequestContext>());
                return ValueTask.FromResult(new CustomResult(request.Value));
            },
            CustomJsonContext.Default.CustomRequest, CustomJsonContext.Default.CustomResult);
        var registry = builder.Build();
        var rpcBindings = new A2AJsonRpcCustomOperationBuilder().Map("debate/resume", resume).Build(registry);
        var httpBindings = new A2AHttpCustomOperationBuilder()
            .Map(HttpMethods.Post, "/debate:resume", resume,
                (_, _) => ValueTask.FromResult(new CustomRequest("debate-42")))
            .Build(registry);
        var policyCalls = 0;
        var extensions = new A2AExtensionOptions(registry)
        {
            CanActivate = (httpContext, extension, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.Equal("tenant-a", httpContext.Request.Headers["x-tenant"].ToString());
                Assert.Same(debate, extension);
                policyCalls++;
                return ValueTask.FromResult(true);
            },
        };
        await using var app = CreateApp();
        app.MapA2A(Mock.Of<IA2ARequestHandler>(), "/rpc", extensions, rpcBindings);
        app.MapHttpA2A(Mock.Of<IA2ARequestHandler>(), "/http", extensions, httpBindings);
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/debate:resume");
        context.Request.Headers[HeaderName] = ExtensionUri;
        context.Request.Headers["x-tenant"] = "tenant-a";
        if (jsonRpc) SetRpcBody(context, "debate/resume");

        await FindEndpoint(app, context.Request.Path).RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(ExtensionUri, context.Response.Headers[HeaderName].ToString());
        Assert.Equal(ExtensionUri, Assert.Single(seen!.ActivatedExtensions));
        Assert.Equal(1, policyCalls);
        Assert.Contains("debate-42", await ReadBodyAsync(context), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingRegistryOverloads_ActivateRequestedExtensionsWithoutAPolicy(bool jsonRpc)
    {
        var (registry, unary, _) = CreateRegistry();
        await using var app = CreateApp();
        if (jsonRpc)
        {
            app.MapA2A(Mock.Of<IA2ARequestHandler>(), "/rpc", registry,
                new A2AJsonRpcCustomOperationBuilder().Map("debate/resume", unary).Build(registry));
        }
        else
        {
            app.MapHttpA2A(Mock.Of<IA2ARequestHandler>(), "/http", registry,
                new A2AHttpCustomOperationBuilder().Map(HttpMethods.Post, "/debate:resume", unary, BindRequest).Build(registry));
        }
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/debate:resume");
        context.Request.Headers[HeaderName] = ExtensionUri;
        if (jsonRpc) SetRpcBody(context, "debate/resume");

        await FindEndpoint(app, context.Request.Path).RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(ExtensionUri, context.Response.Headers[HeaderName].ToString());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task InactiveExtension_RejectsUnaryAndStreamingBeforeHandlerExecution(bool jsonRpc, bool streaming)
    {
        var calls = 0;
        var (registry, unary, stream) = CreateRegistry(onInvoke: _ => calls++);
        await using var app = CreateApp();
        MapBoth(app, new A2AExtensionOptions(registry), unary, stream);
        var context = CreateContext(app, jsonRpc ? "/rpc" : streaming ? "/http/debate:watch" : "/http/debate:resume");
        if (jsonRpc) SetRpcBody(context, streaming ? "debate/watch" : "debate/resume");

        await FindEndpoint(app, context.Request.Path).RequestDelegate!(context);

        Assert.Equal(0, calls);
        Assert.False(context.Response.Headers.ContainsKey(HeaderName));
        Assert.NotEqual("text/event-stream", context.Response.ContentType);
        await AssertErrorAsync(context, jsonRpc, A2AErrorCode.ExtensionSupportRequired, "EXTENSION_SUPPORT_REQUIRED");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HeaderParsing_HandlesRepeatedValuesWhitespaceDuplicatesAndUnknownVersions(bool jsonRpc)
    {
        var calls = 0;
        var (registry, unary, stream) = CreateRegistry();
        await using var app = CreateApp();
        MapBoth(app, new A2AExtensionOptions(registry)
        {
            CanActivate = (_, _, _) => { calls++; return ValueTask.FromResult(true); },
        }, unary, stream);
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/debate:resume");
        context.Request.Headers["a2a-extensions"] = new StringValues([
            $" , \t{ExtensionUri} \t, https://example.org/extensions/debate/v2,",
            $"{ExtensionUri}, {ExtensionUri.ToUpperInvariant()}, https://example.org/extensions/unknown/v1",
        ]);
        if (jsonRpc) SetRpcBody(context, "debate/resume");

        await FindEndpoint(app, context.Request.Path).RequestDelegate!(context);

        Assert.Equal(ExtensionUri, context.Response.Headers[HeaderName].ToString());
        Assert.Equal(1, calls);
        var negotiated = context.Features.Get<A2ARequestContext>()!;
        Assert.Equal(4, negotiated.RequestedExtensions.Count);
        Assert.Equal(ExtensionUri, Assert.Single(negotiated.ActivatedExtensions));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeclinedOptionalExtension_IsNotReportedAsActive(bool jsonRpc)
    {
        var (registry, unary, stream) = CreateRegistry();
        await using var app = CreateApp();
        MapBoth(app, new A2AExtensionOptions(registry)
        {
            CanActivate = (_, _, _) => ValueTask.FromResult(false),
        }, unary, stream);
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/debate:resume");
        context.Request.Headers[HeaderName] = ExtensionUri;
        if (jsonRpc) SetRpcBody(context, "debate/resume");

        await FindEndpoint(app, context.Request.Path).RequestDelegate!(context);

        Assert.Empty(context.Features.Get<A2ARequestContext>()!.ActivatedExtensions);
        Assert.False(context.Response.Headers.ContainsKey(HeaderName));
        await AssertErrorAsync(context, jsonRpc, A2AErrorCode.ExtensionSupportRequired, "EXTENSION_SUPPORT_REQUIRED");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NegotiationFailure_DoesNotPublishAPartialActivatedSet(bool jsonRpc)
    {
        var (registry, unary, stream) = CreateRegistry(requiredOther: true);
        await using var app = CreateApp();
        MapBoth(app, new A2AExtensionOptions(registry)
        {
            CanActivate = (_, extension, _) => ValueTask.FromResult(extension.Uri != OtherUri),
        }, unary, stream);
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/debate:resume");
        context.Request.Headers[HeaderName] = $"{ExtensionUri}, {OtherUri}";
        if (jsonRpc) SetRpcBody(context, "debate/resume");

        await FindEndpoint(app, context.Request.Path).RequestDelegate!(context);

        Assert.Null(context.Features.Get<A2ARequestContext>());
        Assert.False(context.Response.Headers.ContainsKey(HeaderName));
        await AssertErrorAsync(context, jsonRpc, A2AErrorCode.ExtensionSupportRequired, "EXTENSION_SUPPORT_REQUIRED");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OperationFailure_PreservesActivationAndSanitizesUnexpectedErrors(bool jsonRpc)
    {
        var (registry, unary, stream) = CreateRegistry(onInvoke: _ => throw new InvalidOperationException("secret"));
        await using var app = CreateApp();
        MapBoth(app, new A2AExtensionOptions(registry), unary, stream);
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/debate:resume");
        context.Request.Headers[HeaderName] = ExtensionUri;
        if (jsonRpc) SetRpcBody(context, "debate/resume");

        await FindEndpoint(app, context.Request.Path).RequestDelegate!(context);

        Assert.Equal(ExtensionUri, context.Response.Headers[HeaderName].ToString());
        await AssertErrorAsync(context, jsonRpc, A2AErrorCode.InternalError, null);
        Assert.DoesNotContain("secret", await ReadBodyAsync(context), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidVersion_PrecedesNegotiation(bool jsonRpc)
    {
        var calls = 0;
        var (registry, unary, stream) = CreateRegistry();
        await using var app = CreateApp();
        MapBoth(app, new A2AExtensionOptions(registry)
        {
            CanActivate = (_, _, _) => { calls++; return ValueTask.FromResult(true); },
        }, unary, stream);
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/debate:resume");
        context.Request.Headers[HeaderName] = ExtensionUri;
        context.Request.Headers["A2A-Version"] = "99.0";
        if (jsonRpc) SetRpcBody(context, "debate/resume");

        await FindEndpoint(app, context.Request.Path).RequestDelegate!(context);

        Assert.Equal(0, calls);
        Assert.Null(context.Features.Get<A2ARequestContext>());
        Assert.False(context.Response.Headers.ContainsKey(HeaderName));
        await AssertErrorAsync(context, jsonRpc, A2AErrorCode.VersionNotSupported, "VERSION_NOT_SUPPORTED");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StandardOperations_ReceiveTheSameSnapshotAndRequiredExtensionGate(bool jsonRpc)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri, Required = true });
        var options = new A2AExtensionOptions(builder.Build());
        A2ARequestContext? seen = null;
        using var cancellation = new CancellationTokenSource();
        var handler = new Mock<IA2ARequestHandler>();
        handler.Setup(h => h.GetTaskAsync(It.IsAny<GetTaskRequest>(), It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()))
            .Returns((GetTaskRequest _, A2ARequestContext? requestContext, CancellationToken token) =>
            {
                Assert.Equal(cancellation.Token, token);
                seen = requestContext;
                return Task.FromResult(new AgentTask { Id = "task-42", ContextId = "context-42", Status = new A2A.TaskStatus { State = TaskState.Completed } });
            });
        await using var app = CreateApp();
        app.MapA2A(handler.Object, "/rpc", options);
        app.MapHttpA2A(handler.Object, "/http", options);
        var current = CreateContext(app, jsonRpc ? "/rpc" : "/http/tasks/{id}", jsonRpc ? HttpMethods.Post : HttpMethods.Get);
        current.Request.RouteValues["id"] = "task-42";
        if (jsonRpc) SetRpcBody(current, A2AMethods.GetTask, "{\"id\":\"task-42\"}");
        await FindEndpoint(app, current.Request.Path).RequestDelegate!(current);
        handler.Verify(h => h.GetTaskAsync(It.IsAny<GetTaskRequest>(), It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()), Times.Never);
        await AssertErrorAsync(current, jsonRpc, A2AErrorCode.ExtensionSupportRequired, "EXTENSION_SUPPORT_REQUIRED");

        current = CreateContext(app, jsonRpc ? "/rpc" : "/http/tasks/{id}", jsonRpc ? HttpMethods.Post : HttpMethods.Get);
        current.RequestAborted = cancellation.Token;
        current.Request.RouteValues["id"] = "task-42";
        current.Request.Headers[HeaderName] = ExtensionUri;
        if (jsonRpc) SetRpcBody(current, A2AMethods.GetTask, "{\"id\":\"task-42\"}");
        await FindEndpoint(app, current.Request.Path).RequestDelegate!(current);

        Assert.Same(current.Features.Get<A2ARequestContext>(), seen);
        Assert.Equal(ExtensionUri, Assert.Single(seen!.ActivatedExtensions));
        Assert.Equal(ExtensionUri, current.Response.Headers[HeaderName].ToString());
        handler.Verify(h => h.GetTaskAsync(It.IsAny<GetTaskRequest>(), seen, cancellation.Token), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PolicyFailure_ReturnsASanitizedErrorWithoutPublishingActivation(bool jsonRpc)
    {
        var (registry, unary, stream) = CreateRegistry(requiredOther: true);
        await using var app = CreateApp();
        MapBoth(app, new A2AExtensionOptions(registry)
        {
            CanActivate = (_, extension, _) => extension.Uri == OtherUri
                ? throw new InvalidOperationException("secret policy failure")
                : ValueTask.FromResult(true),
        }, unary, stream);
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/debate:resume");
        context.Request.Headers[HeaderName] = $"{ExtensionUri}, {OtherUri}";
        if (jsonRpc) SetRpcBody(context, "debate/resume");

        await FindEndpoint(app, context.Request.Path).RequestDelegate!(context);

        Assert.Null(context.Features.Get<A2ARequestContext>());
        Assert.False(context.Response.Headers.ContainsKey(HeaderName));
        await AssertErrorAsync(context, jsonRpc, A2AErrorCode.InternalError, null);
        Assert.DoesNotContain("secret", await ReadBodyAsync(context), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidIdentifier_RejectsBeforeAvailabilityEvaluation(bool jsonRpc)
    {
        var policyCalls = 0;
        var (registry, unary, stream) = CreateRegistry();
        await using var app = CreateApp();
        MapBoth(app, new A2AExtensionOptions(registry)
        {
            CanActivate = (_, _, _) => { policyCalls++; return ValueTask.FromResult(true); },
        }, unary, stream);
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/debate:resume");
        context.Request.Headers[HeaderName] = $"{ExtensionUri}, https://example.org/invalid identifier";
        if (jsonRpc) SetRpcBody(context, "debate/resume");

        await FindEndpoint(app, context.Request.Path).RequestDelegate!(context);

        Assert.Equal(0, policyCalls);
        Assert.Null(context.Features.Get<A2ARequestContext>());
        Assert.False(context.Response.Headers.ContainsKey(HeaderName));
        await AssertErrorAsync(context, jsonRpc, A2AErrorCode.InvalidRequest, null);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task StreamingFailure_PreservesSnapshotAndHeaderBeforeOrAfterFirstEvent(bool jsonRpc, bool yieldFirst)
    {
        var available = true;
        var policyCalls = 0;
        var disposed = false;
        A2ARequestContext? seen = null;
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var stream = builder.MapStreaming<CustomRequest, CustomResult>(extension,
            new A2AOperationId("https://example.org/operations#watch"), Stream,
            CustomJsonContext.Default.CustomRequest, CustomJsonContext.Default.CustomResult);
        var registry = builder.Build();
        var options = new A2AExtensionOptions(registry)
        {
            CanActivate = (_, _, _) => { policyCalls++; return ValueTask.FromResult(available); },
        };
        await using var app = CreateApp();
        app.MapA2A(Mock.Of<IA2ARequestHandler>(), "/rpc", options,
            new A2AJsonRpcCustomOperationBuilder().MapStreaming("debate/watch", stream).Build(registry));
        app.MapHttpA2A(Mock.Of<IA2ARequestHandler>(), "/http", options,
            new A2AHttpCustomOperationBuilder().MapStreaming(HttpMethods.Post, "/debate:watch", stream, BindRequest).Build(registry));
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/debate:watch");
        context.Request.Headers[HeaderName] = ExtensionUri;
        if (jsonRpc) SetRpcBody(context, "debate/watch");
        var endpoint = FindEndpoint(app, context.Request.Path);

        await endpoint.RequestDelegate!(context);

        Assert.True(disposed);
        Assert.Equal(1, policyCalls);
        Assert.Same(seen, context.Features.Get<A2ARequestContext>());
        Assert.Equal(ExtensionUri, Assert.Single(seen!.ActivatedExtensions));
        Assert.Equal(ExtensionUri, context.Response.Headers[HeaderName].ToString());
        var body = await ReadBodyAsync(context);
        if (yieldFirst)
        {
            Assert.Equal("text/event-stream", context.Response.ContentType);
            Assert.Contains("debate-42", body, StringComparison.Ordinal);
            Assert.Contains(jsonRpc ? "\"error\"" : "event: error", body, StringComparison.Ordinal);
        }
        else
        {
            Assert.NotEqual("text/event-stream", context.Response.ContentType);
            await AssertErrorAsync(context, jsonRpc, A2AErrorCode.InvalidParams, "INVALID_PARAMS");
        }
        Assert.Contains("dependency unavailable", body, StringComparison.Ordinal);

        var next = CreateContext(app, context.Request.Path);
        next.Request.Headers[HeaderName] = ExtensionUri;
        if (jsonRpc) SetRpcBody(next, "debate/watch");
        await endpoint.RequestDelegate!(next);
        Assert.Empty(next.Features.Get<A2ARequestContext>()!.ActivatedExtensions);
        Assert.Equal(ExtensionUri, Assert.Single(seen.ActivatedExtensions));
        Assert.Equal(2, policyCalls);

        async IAsyncEnumerable<CustomResult> Stream(A2ACustomOperationContext operationContext,
            CustomRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            seen = operationContext.GetRequiredFeature<A2ARequestContext>();
            try
            {
                available = false;
                if (yieldFirst) yield return new CustomResult(request.Value);
                await Task.Yield();
                throw new A2AException("dependency unavailable", A2AErrorCode.InvalidParams);
            }
            finally { disposed = true; }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentRequests_DoNotShareActivationState(bool jsonRpc)
    {
        var (registry, unary, stream) = CreateRegistry();
        await using var app = CreateApp();
        MapBoth(app, new A2AExtensionOptions(registry)
        {
            CanActivate = async (context, _, cancellationToken) =>
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                return context.Request.Headers["x-allow"] == "true";
            },
        }, unary, stream);
        var path = jsonRpc ? "/rpc" : "/http/debate:resume";
        var endpoint = FindEndpoint(app, path);
        var contexts = await Task.WhenAll(Enumerable.Range(0, 40).Select(async index =>
        {
            var context = CreateContext(app, path);
            context.Request.Headers[HeaderName] = ExtensionUri;
            context.Request.Headers["x-allow"] = index % 2 == 0 ? "true" : "false";
            if (jsonRpc) SetRpcBody(context, "debate/resume");
            await endpoint.RequestDelegate!(context);
            Assert.Equal(index % 2 == 0, context.Features.Get<A2ARequestContext>()!.ActivatedExtensions.Contains(ExtensionUri));
            Assert.Equal(index % 2 == 0, context.Response.Headers.ContainsKey(HeaderName));
            return context.Features.Get<A2ARequestContext>()!;
        }));

        Assert.Equal(40, contexts.Distinct().Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RequestCancellation_ReachesPolicyWithoutBecomingAnInternalError(bool jsonRpc)
    {
        var (registry, unary, stream) = CreateRegistry();
        using var cancellation = new CancellationTokenSource();
        var policyCalled = false;
        await using var app = CreateApp();
        MapBoth(app, new A2AExtensionOptions(registry)
        {
            CanActivate = async (_, _, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                policyCalled = true;
                await cancellation.CancelAsync();
                return true;
            },
        }, unary, stream);
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/debate:resume");
        context.RequestAborted = cancellation.Token;
        context.Request.Headers[HeaderName] = ExtensionUri;
        if (jsonRpc) SetRpcBody(context, "debate/resume");
        var endpoint = FindEndpoint(app, context.Request.Path);

        if (jsonRpc) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => endpoint.RequestDelegate!(context));
        else await endpoint.RequestDelegate!(context);

        Assert.True(policyCalled);
        Assert.Null(context.Features.Get<A2ARequestContext>());
        Assert.False(context.Response.Headers.ContainsKey(HeaderName));
        Assert.Empty(await ReadBodyAsync(context));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandlerOnlyOverloads_RemainNegotiationFree(bool jsonRpc)
    {
        var handler = new Mock<IA2ARequestHandler>();
        handler.Setup(h => h.GetTaskAsync(It.IsAny<GetTaskRequest>(), It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTask { Id = "task-42", ContextId = "context-42", Status = new A2A.TaskStatus { State = TaskState.Completed } });
        await using var app = CreateApp();
        app.MapA2A(handler.Object, "/rpc");
        app.MapHttpA2A(handler.Object, "/http");
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/tasks/{id}", jsonRpc ? HttpMethods.Post : HttpMethods.Get);
        context.Request.RouteValues["id"] = "task-42";
        context.Request.Headers[HeaderName] = "not an extension identifier";
        if (jsonRpc) SetRpcBody(context, A2AMethods.GetTask, "{\"id\":\"task-42\"}");

        await FindEndpoint(app, context.Request.Path).RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Null(context.Features.Get<A2ARequestContext>());
        Assert.False(context.Response.Headers.ContainsKey(HeaderName));
        handler.Verify(h => h.GetTaskAsync(It.IsAny<GetTaskRequest>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActiveStreamingOperation_PreservesTheExistingSseEnvelope(bool jsonRpc)
    {
        var (registry, unary, stream) = CreateRegistry();
        await using var app = CreateApp();
        MapBoth(app, new A2AExtensionOptions(registry), unary, stream);
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/debate:watch");
        context.Request.Headers[HeaderName] = ExtensionUri;
        if (jsonRpc) SetRpcBody(context, "debate/watch");

        await FindEndpoint(app, context.Request.Path).RequestDelegate!(context);

        Assert.Equal("text/event-stream", context.Response.ContentType);
        Assert.Equal(ExtensionUri, context.Response.Headers[HeaderName].ToString());
        var body = await ReadBodyAsync(context);
        Assert.Contains("debate-42", body, StringComparison.Ordinal);
        Assert.Equal(jsonRpc, body.Contains("\"jsonrpc\"", StringComparison.Ordinal));
        Assert.DoesNotContain("error", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidCustomBody_AfterNegotiationPreservesActivation(bool jsonRpc)
    {
        var (registry, unary, _) = CreateRegistry(onInvoke: _ => Assert.Fail("Handler must not run."));
        await using var app = CreateApp();
        app.MapA2A(Mock.Of<IA2ARequestHandler>(), "/rpc", new A2AExtensionOptions(registry),
            new A2AJsonRpcCustomOperationBuilder().Map("debate/resume", unary).Build(registry));
        app.MapHttpA2A(Mock.Of<IA2ARequestHandler>(), "/http", new A2AExtensionOptions(registry),
            new A2AHttpCustomOperationBuilder().Map(HttpMethods.Post, "/debate:resume", unary,
                async (context, ct) => (await JsonSerializer.DeserializeAsync(context.Request.Body, CustomJsonContext.Default.CustomRequest, ct))!).Build(registry));
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/debate:resume");
        context.Request.Headers[HeaderName] = ExtensionUri;
        if (jsonRpc) SetRpcBody(context, "debate/resume", "{\"value\":42}");
        else context.Request.Body = new MemoryStream("{"u8.ToArray());

        await FindEndpoint(app, context.Request.Path).RequestDelegate!(context);

        Assert.Equal(ExtensionUri, context.Response.Headers[HeaderName].ToString());
        await AssertErrorAsync(context, jsonRpc, A2AErrorCode.InvalidParams, "INVALID_PARAMS");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeclinedProfileExtension_DoesNotBlockAnUnownedStandardOperation(bool jsonRpc)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var options = new A2AExtensionOptions(builder.Build())
        {
            CanActivate = (_, _, _) => ValueTask.FromResult(false),
        };
        var handler = new Mock<IA2ARequestHandler>();
        handler.Setup(h => h.GetTaskAsync(It.IsAny<GetTaskRequest>(), It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTask { Id = "task-42", ContextId = "context-42", Status = new A2A.TaskStatus { State = TaskState.Completed } });
        await using var app = CreateApp();
        app.MapA2A(handler.Object, "/rpc", options);
        app.MapHttpA2A(handler.Object, "/http", options);
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/tasks/{id}", jsonRpc ? HttpMethods.Post : HttpMethods.Get);
        context.Request.RouteValues["id"] = "task-42";
        context.Request.Headers[HeaderName] = ExtensionUri;
        if (jsonRpc) SetRpcBody(context, A2AMethods.GetTask, "{\"id\":\"task-42\"}");

        await FindEndpoint(app, context.Request.Path).RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Empty(context.Features.Get<A2ARequestContext>()!.ActivatedExtensions);
        Assert.False(context.Response.Headers.ContainsKey(HeaderName));
        handler.Verify(h => h.GetTaskAsync(It.IsAny<GetTaskRequest>(), It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StandardOperationError_PreservesActivationAndExistingErrorCode(bool jsonRpc)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var options = new A2AExtensionOptions(builder.Build());
        var handler = new Mock<IA2ARequestHandler>();
        handler.Setup(h => h.GetTaskAsync(It.IsAny<GetTaskRequest>(), It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new A2AException("task not found", A2AErrorCode.TaskNotFound));
        await using var app = CreateApp();
        app.MapA2A(handler.Object, "/rpc", options);
        app.MapHttpA2A(handler.Object, "/http", options);
        var context = CreateContext(app, jsonRpc ? "/rpc" : "/http/tasks/{id}", jsonRpc ? HttpMethods.Post : HttpMethods.Get);
        context.Request.RouteValues["id"] = "task-42";
        context.Request.Headers[HeaderName] = ExtensionUri;
        if (jsonRpc) SetRpcBody(context, A2AMethods.GetTask, "{\"id\":\"task-42\"}");

        await FindEndpoint(app, context.Request.Path).RequestDelegate!(context);

        Assert.Equal(ExtensionUri, context.Response.Headers[HeaderName].ToString());
        await AssertErrorAsync(context, jsonRpc, A2AErrorCode.TaskNotFound, "TASK_NOT_FOUND");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mapping_RejectsBindingsFromADifferentAuthoritativeRegistry(bool jsonRpc)
    {
        var (registry, unary, _) = CreateRegistry();
        var wrongOptions = new A2AExtensionOptions(new A2ACustomOperationRegistryBuilder().Build());
        await using var app = CreateApp();
        if (jsonRpc)
        {
            var bindings = new A2AJsonRpcCustomOperationBuilder().Map("debate/resume", unary).Build(registry);
            Assert.Throws<InvalidOperationException>(() => app.MapA2A(Mock.Of<IA2ARequestHandler>(), "/rpc", wrongOptions, bindings));
        }
        else
        {
            var bindings = new A2AHttpCustomOperationBuilder().Map(HttpMethods.Post, "/debate:resume", unary, BindRequest).Build(registry);
            Assert.Throws<InvalidOperationException>(() => app.MapHttpA2A(Mock.Of<IA2ARequestHandler>(), "/http", wrongOptions, bindings));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StandardStreamingOperation_CanReadTheSnapshotDuringEnumeration(bool jsonRpc)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var options = new A2AExtensionOptions(builder.Build());
        A2ARequestContext? seen = null;
        using var cancellation = new CancellationTokenSource();
        var handler = new Mock<IA2ARequestHandler>();
        handler.Setup(h => h.SendStreamingMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()))
            .Returns((SendMessageRequest _, A2ARequestContext? requestContext, CancellationToken token) => Stream(requestContext, token));
        await using var app = CreateApp();
        app.MapA2A(handler.Object, "/rpc", options);
        app.MapHttpA2A(handler.Object, "/http", options);
        var current = CreateContext(app, jsonRpc ? "/rpc" : "/http/message:stream");
        current.RequestAborted = cancellation.Token;
        current.Request.Headers[HeaderName] = ExtensionUri;
        var parameters = JsonSerializer.Serialize(new SendMessageRequest
        {
            Message = new Message { MessageId = "client-1", Role = Role.User, Parts = [new Part { Text = "hello" }] },
        }, A2AJsonUtilities.DefaultOptions);
        if (jsonRpc) SetRpcBody(current, A2AMethods.SendStreamingMessage, parameters);
        else
        {
            var bytes = Encoding.UTF8.GetBytes(parameters);
            current.Request.Body = new MemoryStream(bytes);
            current.Request.ContentLength = bytes.Length;
            current.Request.ContentType = "application/json";
            current.Features.Set(Mock.Of<IHttpRequestBodyDetectionFeature>(feature => feature.CanHaveBody));
        }

        await FindEndpoint(app, current.Request.Path).RequestDelegate!(current);

        Assert.Same(seen, current.Features.Get<A2ARequestContext>());
        Assert.Equal(ExtensionUri, Assert.Single(seen!.ActivatedExtensions));
        Assert.Equal(ExtensionUri, current.Response.Headers[HeaderName].ToString());
        Assert.Equal("text/event-stream", current.Response.ContentType);
        Assert.Contains("agent reply", await ReadBodyAsync(current), StringComparison.Ordinal);

        async IAsyncEnumerable<StreamResponse> Stream(A2ARequestContext? requestContext, [EnumeratorCancellation] CancellationToken token)
        {
            Assert.Equal(cancellation.Token, token);
            token.ThrowIfCancellationRequested();
            await Task.Yield();
            seen = requestContext;
            yield return new StreamResponse
            {
                Message = new Message { MessageId = "agent-1", Role = Role.Agent, Parts = [new Part { Text = "agent reply" }] },
            };
        }
    }

    [Fact]
    public async Task Processor_LegacyExplicitNullArgumentsRemainSourceCompatibleAndNegotiationFree()
    {
        var handler = new Mock<IA2ARequestHandler>();
        handler.Setup(h => h.GetTaskAsync(It.IsAny<GetTaskRequest>(), It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTask { Id = "task-42", ContextId = "context-42" });
        await using var app = CreateApp();
        var context = CreateContext(app, "/rpc");
        context.Request.Headers[HeaderName] = "not an extension identifier";
        SetRpcBody(context, A2AMethods.GetTask, "{\"id\":\"task-42\"}");

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            handler.Object, context.Request, null, null, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Null(context.Features.Get<A2ARequestContext>());
        Assert.False(context.Response.Headers.ContainsKey(HeaderName));
        Assert.Contains("task-42", await ReadBodyAsync(context), StringComparison.Ordinal);
        handler.Verify(h => h.GetTaskAsync(It.IsAny<GetTaskRequest>(), It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Processor_ExtensionAwareEntryRejectsForeignBindingsBeforeReadingTheRequest()
    {
        var (registry, unary, _) = CreateRegistry();
        var bindings = new A2AJsonRpcCustomOperationBuilder().Map("debate/resume", unary).Build(registry);
        var wrongOptions = new A2AExtensionOptions(new A2ACustomOperationRegistryBuilder().Build());
        await using var app = CreateApp();
        var context = CreateContext(app, "/rpc");

        await Assert.ThrowsAsync<InvalidOperationException>(() => A2AJsonRpcProcessor.ProcessRequestWithExtensionsAsync(
            Mock.Of<IA2ARequestHandler>(), context.Request, wrongOptions, bindings, CancellationToken.None));

        Assert.Equal(0, context.Request.Body.Position);
        Assert.Null(context.Features.Get<A2ARequestContext>());
    }

    private static (A2ACustomOperationRegistry Registry, A2ACustomOperation<CustomRequest, CustomResult> Unary,
        A2AStreamingCustomOperation<CustomRequest, CustomResult> Stream) CreateRegistry(
        Action<A2ARequestContext>? onInvoke = null, bool requiredOther = false)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        if (requiredOther) builder.RegisterExtension(new AgentExtension { Uri = OtherUri, Required = true });
        var unary = builder.Map<CustomRequest, CustomResult>(extension, new A2AOperationId("https://example.org/operations#resume"),
            (context, request, _) =>
            {
                onInvoke?.Invoke(context.GetRequiredFeature<A2ARequestContext>());
                return ValueTask.FromResult(new CustomResult(request.Value));
            }, CustomJsonContext.Default.CustomRequest, CustomJsonContext.Default.CustomResult);
        var stream = builder.MapStreaming<CustomRequest, CustomResult>(extension, new A2AOperationId("https://example.org/operations#watch"),
            (context, request, ct) => StreamResults(context.GetRequiredFeature<A2ARequestContext>(), request, onInvoke, ct),
            CustomJsonContext.Default.CustomRequest, CustomJsonContext.Default.CustomResult);
        return (builder.Build(), unary, stream);
    }

    private static async IAsyncEnumerable<CustomResult> StreamResults(A2ARequestContext context, CustomRequest request,
        Action<A2ARequestContext>? onInvoke, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        onInvoke?.Invoke(context);
        yield return new CustomResult(request.Value);
        await Task.Yield();
    }

    private static ValueTask<CustomRequest> BindRequest(HttpContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(new CustomRequest("debate-42"));

    private static void MapBoth(WebApplication app, A2AExtensionOptions options,
        A2ACustomOperation<CustomRequest, CustomResult> unary, A2AStreamingCustomOperation<CustomRequest, CustomResult> stream)
    {
        var registry = options.Registry;
        app.MapA2A(Mock.Of<IA2ARequestHandler>(), "/rpc", options,
            new A2AJsonRpcCustomOperationBuilder().Map("debate/resume", unary).MapStreaming("debate/watch", stream).Build(registry));
        app.MapHttpA2A(Mock.Of<IA2ARequestHandler>(), "/http", options,
            new A2AHttpCustomOperationBuilder().Map(HttpMethods.Post, "/debate:resume", unary, BindRequest)
                .MapStreaming(HttpMethods.Post, "/debate:watch", stream, BindRequest).Build(registry));
    }

    private static WebApplication CreateApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Add(A2AJsonUtilities.DefaultOptions.TypeInfoResolver!));
        return builder.Build();
    }

    private static RouteEndpoint FindEndpoint(WebApplication app, PathString route)
        => Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText == route.Value);

    private static DefaultHttpContext CreateContext(WebApplication app, string path, string method = "POST")
    {
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static void SetRpcBody(HttpContext context, string method, string parameters = "{\"value\":\"debate-42\"}")
    {
        var bytes = Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"id\":\"ext-1\",\"method\":\"{method}\",\"params\":{parameters}}}");
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Request.ContentType = "application/json";
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await new StreamReader(context.Response.Body, leaveOpen: true).ReadToEndAsync();
    }

    private static async Task AssertErrorAsync(HttpContext context, bool jsonRpc, A2AErrorCode code, string? reason)
    {
        using var document = JsonDocument.Parse(await ReadBodyAsync(context));
        var error = document.RootElement.GetProperty("error");
        if (jsonRpc)
        {
            Assert.Equal((int)code, error.GetProperty("code").GetInt32());
            Assert.Equal(code == A2AErrorCode.VersionNotSupported ? null : "ext-1", document.RootElement.GetProperty("id").GetString());
        }
        else
        {
            Assert.Equal(code == A2AErrorCode.InternalError ? 500 : code == A2AErrorCode.TaskNotFound ? 404 : 400, context.Response.StatusCode);
            if (reason is not null) Assert.Equal(reason, error.GetProperty("details")[0].GetProperty("reason").GetString());
        }
    }
}

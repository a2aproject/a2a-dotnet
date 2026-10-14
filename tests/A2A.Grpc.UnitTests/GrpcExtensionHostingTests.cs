namespace A2A.Grpc.UnitTests;

using A2A.Grpc.AspNetCore;
using A2A.Grpc.Extensions.Protos;
using global::Google.Protobuf;
using global::Grpc.Core;
using global::Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;

public sealed class GrpcExtensionHostingTests
{
    private const string ExtensionUri = "https://example.org/extensions/debate/v1";
    private const string OtherUri = "https://example.org/extensions/usage/v1";
    private const string UnaryId = "https://example.org/operations#resume";
    private const string StreamId = "https://example.org/operations#watch";
    private const string MetadataKey = "a2a-extensions";

    [Fact]
    public async Task UsageExample_NegotiatesAndInvokesTheRegisteredOperation()
    {
        var policyCalls = 0;
        ServerCallContext? seenCall = null;
        A2ARequestContext? seenActivation = null;
        await using var host = await Host.StartAsync(
            canActivate: (callContext, extension, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.Equal("tenant-a", callContext.RequestHeaders.GetValue("x-tenant"));
                Assert.Equal(ExtensionUri, extension.Uri);
                seenCall = callContext;
                policyCalls++;
                return ValueTask.FromResult(true);
            },
            onInvoke: context =>
            {
                seenActivation = context.GetRequiredFeature<A2ARequestContext>();
                Assert.Same(seenCall, context.GetRequiredFeature<ServerCallContext>());
                Assert.Same(seenActivation, context.GetRequiredFeature<HttpContext>().Features.Get<A2ARequestContext>());
            });
        var headers = new Metadata { { MetadataKey, ExtensionUri }, { "x-tenant", "tenant-a" } };
        using var call = host.Client.InvokeExtensionOperationAsync(Request(UnaryId), headers);

        var response = await call.ResponseAsync;
        var responseHeaders = await call.ResponseHeadersAsync;

        Assert.Equal("debate-42", JsonSerializer.Deserialize(response.Payload.Span, GrpcCustomJsonContext.Default.GrpcCustomResult)!.Value);
        Assert.Equal(ExtensionUri, responseHeaders.GetValue(MetadataKey));
        Assert.Equal(ExtensionUri, call.GetTrailers().GetValue(MetadataKey));
        Assert.Equal(ExtensionUri, Assert.Single(seenActivation!.ActivatedExtensions));
        Assert.Equal(1, policyCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingActivation_RejectsBeforeUnaryOrStreamingHandler(bool streaming)
    {
        var calls = 0;
        await using var host = await Host.StartAsync(onInvoke: _ => calls++);
        RpcException error;
        Metadata headers;
        if (streaming)
        {
            using var call = host.Client.InvokeStreamingExtensionOperation(Request(StreamId));
            error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext());
            headers = await call.ResponseHeadersAsync;
        }
        else
        {
            using var call = host.Client.InvokeExtensionOperationAsync(Request(UnaryId));
            error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
            headers = await call.ResponseHeadersAsync;
        }

        Assert.Equal(0, calls);
        Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
        Assert.Equal(A2AErrorCode.ExtensionSupportRequired, GrpcErrorMapping.ToA2AException(error).ErrorCode);
        Assert.Null(headers.GetValue(MetadataKey));
        Assert.Null(error.Trailers.GetValue(MetadataKey));
    }

    [Fact]
    public async Task MetadataParsing_HandlesRepeatedValuesWhitespaceDuplicatesAndUnknownVersions()
    {
        var calls = 0;
        A2ARequestContext? seen = null;
        await using var host = await Host.StartAsync(
            canActivate: (_, _, _) => { calls++; return ValueTask.FromResult(true); },
            onInvoke: context => seen = context.GetRequiredFeature<A2ARequestContext>());
        var headers = new Metadata
        {
            { MetadataKey, $" , \t{ExtensionUri} \t, https://example.org/extensions/debate/v2," },
            { "A2A-Extensions", $"{ExtensionUri}, {ExtensionUri.ToUpperInvariant()}, https://example.org/extensions/unknown/v1" },
        };
        using var call = host.Client.InvokeExtensionOperationAsync(Request(UnaryId), headers);
        await call.ResponseAsync;

        Assert.Equal(ExtensionUri, (await call.ResponseHeadersAsync).GetValue(MetadataKey));
        Assert.Equal(4, seen!.RequestedExtensions.Count);
        Assert.Equal(ExtensionUri, Assert.Single(seen.ActivatedExtensions));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DeclinedOptionalExtension_IsNotReportedAsActive()
    {
        await using var host = await Host.StartAsync(canActivate: (_, _, _) => ValueTask.FromResult(false));
        using var call = host.Client.InvokeExtensionOperationAsync(Request(UnaryId), ActiveHeaders());

        var error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);

        Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
        Assert.Null((await call.ResponseHeadersAsync).GetValue(MetadataKey));
        Assert.Null(error.Trailers.GetValue(MetadataKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NegotiationFailure_DoesNotPublishAPartialActivatedSet(bool policyThrows)
    {
        ServerCallContext? seenCall = null;
        await using var host = await Host.StartAsync(requiredOther: true,
            canActivate: (context, extension, _) =>
            {
                seenCall = context;
                Assert.Null(context.GetHttpContext().Features.Get<A2ARequestContext>());
                if (extension.Uri == OtherUri)
                {
                    if (policyThrows) throw new InvalidOperationException("secret policy failure");
                    return ValueTask.FromResult(false);
                }
                return ValueTask.FromResult(true);
            });
        using var call = host.Client.InvokeExtensionOperationAsync(Request(UnaryId),
            new Metadata { { MetadataKey, $"{ExtensionUri}, {OtherUri}" } });

        var error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);

        Assert.Equal(policyThrows ? StatusCode.Internal : StatusCode.FailedPrecondition, error.StatusCode);
        Assert.Null((await call.ResponseHeadersAsync).GetValue(MetadataKey));
        Assert.Null(error.Trailers.GetValue(MetadataKey));
        Assert.NotNull(seenCall);
        Assert.DoesNotContain("secret", error.Status.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OperationFailure_PreservesActivationAndExistingErrorMapping(bool unexpected)
    {
        await using var host = await Host.StartAsync(onInvoke: _ =>
        {
            if (unexpected) throw new InvalidOperationException("secret operation failure");
            throw new A2AException("invalid debate", A2AErrorCode.InvalidParams);
        });
        using var call = host.Client.InvokeExtensionOperationAsync(Request(UnaryId), ActiveHeaders());

        var error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);

        Assert.Equal(unexpected ? StatusCode.Internal : StatusCode.InvalidArgument, error.StatusCode);
        Assert.Equal(ExtensionUri, (await call.ResponseHeadersAsync).GetValue(MetadataKey));
        Assert.Equal(ExtensionUri, error.Trailers.GetValue(MetadataKey));
        if (!unexpected) Assert.Equal(A2AErrorCode.InvalidParams, GrpcErrorMapping.ToA2AException(error).ErrorCode);
        Assert.DoesNotContain("secret", error.Status.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StandardOperations_EnforceRequiredExtensionsForUnaryAndStreaming(bool streaming)
    {
        await using var host = await Host.StartAsync(requiredDebate: true);
        if (streaming)
        {
            using var rejected = host.StandardClient.SendStreamingMessage(new Protos.SendMessageRequest());
            var error = await Assert.ThrowsAsync<RpcException>(async () => await rejected.ResponseStream.MoveNext());
            Assert.Equal(A2AErrorCode.ExtensionSupportRequired, GrpcErrorMapping.ToA2AException(error).ErrorCode);
            Assert.Null(host.Handler.LastSendMessage);
            using var accepted = host.StandardClient.SendStreamingMessage(StandardMessage(), ActiveHeaders());
            await foreach (var _ in accepted.ResponseStream.ReadAllAsync()) { }
            Assert.NotNull(host.Handler.LastSendMessage);
            Assert.Equal(ExtensionUri, (await accepted.ResponseHeadersAsync).GetValue(MetadataKey));
        }
        else
        {
            using var rejected = host.StandardClient.GetTaskAsync(new Protos.GetTaskRequest { Id = "task-42" });
            var error = await Assert.ThrowsAsync<RpcException>(async () => await rejected.ResponseAsync);
            Assert.Equal(A2AErrorCode.ExtensionSupportRequired, GrpcErrorMapping.ToA2AException(error).ErrorCode);
            Assert.Null(host.Handler.LastGetTask);
            using var accepted = host.StandardClient.GetTaskAsync(new Protos.GetTaskRequest { Id = "task-42" }, ActiveHeaders());
            await accepted.ResponseAsync;
            Assert.NotNull(host.Handler.LastGetTask);
            Assert.Equal(ExtensionUri, (await accepted.ResponseHeadersAsync).GetValue(MetadataKey));
        }
    }

    [Fact]
    public async Task DeclinedOptionalExtension_DoesNotBlockAStandardOperation()
    {
        await using var host = await Host.StartAsync(canActivate: (_, _, _) => ValueTask.FromResult(false));
        using var call = host.StandardClient.GetTaskAsync(new Protos.GetTaskRequest { Id = "task-42" }, ActiveHeaders());

        await call.ResponseAsync;

        Assert.NotNull(host.Handler.LastGetTask);
        Assert.Null((await call.ResponseHeadersAsync).GetValue(MetadataKey));
    }

    [Theory]
    [InlineData("0.3")]
    [InlineData("99.0")]
    public async Task InvalidVersion_PrecedesNegotiation(string version)
    {
        var calls = 0;
        await using var host = await Host.StartAsync(canActivate: (_, _, _) => { calls++; return ValueTask.FromResult(true); });
        var headers = ActiveHeaders();
        headers.Add("a2a-version", version);
        using var call = host.StandardClient.GetTaskAsync(new Protos.GetTaskRequest { Id = "task-42" }, headers);

        var error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);

        Assert.Equal(0, calls);
        Assert.Equal(A2AErrorCode.VersionNotSupported, GrpcErrorMapping.ToA2AException(error).ErrorCode);
        Assert.Null((await call.ResponseHeadersAsync).GetValue(MetadataKey));
        Assert.Null(host.Handler.LastGetTask);
    }

    [Fact]
    public async Task InvalidIdentifier_RejectsBeforeAvailabilityEvaluation()
    {
        var calls = 0;
        await using var host = await Host.StartAsync(canActivate: (_, _, _) => { calls++; return ValueTask.FromResult(true); });
        using var call = host.Client.InvokeExtensionOperationAsync(Request(UnaryId),
            new Metadata { { MetadataKey, $"{ExtensionUri}, https://example.org/invalid identifier" } });

        var error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);

        Assert.Equal(0, calls);
        Assert.Equal(A2AErrorCode.InvalidRequest, GrpcErrorMapping.ToA2AException(error).ErrorCode);
        Assert.Null((await call.ResponseHeadersAsync).GetValue(MetadataKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedVersionValues_CannotBypassPreflight(bool standard)
    {
        var calls = 0;
        await using var host = await Host.StartAsync(canActivate: (_, _, _) => { calls++; return ValueTask.FromResult(true); });
        var headers = ActiveHeaders();
        headers.Add("a2a-version", "1.0");
        headers.Add("a2a-version", "99.0");
        RpcException error;
        Metadata responseHeaders;
        if (standard)
        {
            using var call = host.StandardClient.GetTaskAsync(new Protos.GetTaskRequest { Id = "task-42" }, headers);
            error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
            responseHeaders = await call.ResponseHeadersAsync;
        }
        else
        {
            using var call = host.Client.InvokeExtensionOperationAsync(Request(UnaryId), headers);
            error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
            responseHeaders = await call.ResponseHeadersAsync;
        }

        Assert.Equal(0, calls);
        Assert.Equal(A2AErrorCode.VersionNotSupported, GrpcErrorMapping.ToA2AException(error).ErrorCode);
        Assert.Null(responseHeaders.GetValue(MetadataKey));
    }

    [Fact]
    public async Task ActiveStreamingOperation_ReportsTheSameSnapshotAndMetadata()
    {
        A2ARequestContext? seen = null;
        var policyCalls = 0;
        await using var host = await Host.StartAsync(
            canActivate: (_, _, _) => { policyCalls++; return ValueTask.FromResult(true); },
            onInvoke: context => seen = context.GetRequiredFeature<A2ARequestContext>());
        using var call = host.Client.InvokeStreamingExtensionOperation(Request(StreamId), ActiveHeaders());
        var responses = new List<ExtensionOperationEvent>();

        await foreach (var response in call.ResponseStream.ReadAllAsync()) responses.Add(response);

        Assert.Single(responses);
        Assert.Equal(ExtensionUri, (await call.ResponseHeadersAsync).GetValue(MetadataKey));
        Assert.Equal(ExtensionUri, call.GetTrailers().GetValue(MetadataKey));
        Assert.Equal(ExtensionUri, Assert.Single(seen!.ActivatedExtensions));
        Assert.Equal(1, policyCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamingFailure_BeforeOrAfterFirstEventPreservesActivationAndDisposesTheStream(bool yieldFirst)
    {
        var available = true;
        var disposed = false;
        var policyCalls = 0;
        A2ARequestContext? seen = null;
        await using var host = await Host.StartAsync(
            canActivate: (_, _, _) => { policyCalls++; return ValueTask.FromResult(available); },
            streamFactory: Stream);
        using var call = host.Client.InvokeStreamingExtensionOperation(Request(StreamId), ActiveHeaders());
        if (yieldFirst) Assert.True(await call.ResponseStream.MoveNext());

        var error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext());

        Assert.True(disposed);
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Equal(A2AErrorCode.InvalidParams, GrpcErrorMapping.ToA2AException(error).ErrorCode);
        Assert.Equal(ExtensionUri, (await call.ResponseHeadersAsync).GetValue(MetadataKey));
        Assert.Equal(ExtensionUri, error.Trailers.GetValue(MetadataKey));
        Assert.Equal(ExtensionUri, Assert.Single(seen!.ActivatedExtensions));
        Assert.Equal(1, policyCalls);

        using var next = host.Client.InvokeExtensionOperationAsync(Request(UnaryId), ActiveHeaders());
        var nextError = await Assert.ThrowsAsync<RpcException>(async () => await next.ResponseAsync);
        Assert.Equal(StatusCode.FailedPrecondition, nextError.StatusCode);
        Assert.Null((await next.ResponseHeadersAsync).GetValue(MetadataKey));
        Assert.Equal(ExtensionUri, Assert.Single(seen.ActivatedExtensions));
        Assert.Equal(2, policyCalls);

        async IAsyncEnumerable<GrpcCustomResult> Stream(A2ACustomOperationContext context, GrpcCustomRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            seen = context.GetRequiredFeature<A2ARequestContext>();
            try
            {
                available = false;
                if (yieldFirst) yield return new GrpcCustomResult(request.Value);
                await Task.Yield();
                throw new A2AException("dependency unavailable", A2AErrorCode.InvalidParams);
            }
            finally { disposed = true; }
        }
    }

    [Fact]
    public async Task ConcurrentCalls_DoNotShareActivationDecisions()
    {
        var snapshots = new ConcurrentBag<A2ARequestContext>();
        await using var host = await Host.StartAsync(
            canActivate: async (context, _, cancellationToken) =>
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                return context.RequestHeaders.GetValue("x-allow") == "true";
            },
            onInvoke: context =>
            {
                Assert.Equal("true", context.GetRequiredFeature<ServerCallContext>().RequestHeaders.GetValue("x-allow"));
                snapshots.Add(context.GetRequiredFeature<A2ARequestContext>());
            });
        await Task.WhenAll(Enumerable.Range(0, 40).Select(async index =>
        {
            var headers = ActiveHeaders();
            headers.Add("x-allow", index % 2 == 0 ? "true" : "false");
            using var call = host.Client.InvokeExtensionOperationAsync(Request(UnaryId), headers);
            if (index % 2 == 0) await call.ResponseAsync;
            else
            {
                var error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
                Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
                Assert.Null(error.Trailers.GetValue(MetadataKey));
            }
            Assert.Equal(index % 2 == 0 ? ExtensionUri : null, (await call.ResponseHeadersAsync).GetValue(MetadataKey));
        }));

        Assert.Equal(20, snapshots.Count);
        Assert.Equal(20, snapshots.Distinct().Count());
    }

    [Fact]
    public async Task CancellationDuringPolicy_DoesNotBecomeAnInternalErrorOrPublishActivation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await Host.StartAsync(canActivate: async (_, _, cancellationToken) =>
        {
            Assert.True(cancellationToken.CanBeCanceled);
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { stopped.SetResult(); }
            return true;
        });
        using var cancellation = new CancellationTokenSource();
        using var call = host.Client.InvokeExtensionOperationAsync(Request(UnaryId), ActiveHeaders(), cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await cancellation.CancelAsync();
        var error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);

        Assert.Equal(StatusCode.Cancelled, error.StatusCode);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null((await call.ResponseHeadersAsync).GetValue(MetadataKey));
    }

    [Fact]
    public async Task CancellationDuringStreaming_PreservesTheSnapshotAndDisposesTheStream()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        A2ARequestContext? seen = null;
        await using var host = await Host.StartAsync(streamFactory: Stream);
        using var cancellation = new CancellationTokenSource();
        using var call = host.Client.InvokeStreamingExtensionOperation(Request(StreamId), ActiveHeaders(), cancellationToken: cancellation.Token);
        Assert.True(await call.ResponseStream.MoveNext());
        Assert.Equal(ExtensionUri, (await call.ResponseHeadersAsync).GetValue(MetadataKey));

        await cancellation.CancelAsync();
        var error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext());

        Assert.Equal(StatusCode.Cancelled, error.StatusCode);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ExtensionUri, Assert.Single(seen!.ActivatedExtensions));
        // A disconnected client need not receive final trailers; the server snapshot still persists.

        async IAsyncEnumerable<GrpcCustomResult> Stream(A2ACustomOperationContext context, GrpcCustomRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            seen = context.GetRequiredFeature<A2ARequestContext>();
            try
            {
                yield return new GrpcCustomResult(request.Value);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally { stopped.SetResult(); }
        }
    }

    [Fact]
    public async Task StandardOperationError_PreservesRichStatusAndActivatedMetadata()
    {
        await using var host = await Host.StartAsync();
        host.Handler.Error = new A2AException("task not found", A2AErrorCode.TaskNotFound);
        using var call = host.StandardClient.GetTaskAsync(new Protos.GetTaskRequest { Id = "missing" }, ActiveHeaders());

        var error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);

        Assert.Equal(A2AErrorCode.TaskNotFound, GrpcErrorMapping.ToA2AException(error).ErrorCode);
        Assert.NotNull(error.Trailers.Get("grpc-status-details-bin"));
        Assert.Equal(ExtensionUri, error.Trailers.GetValue(MetadataKey));
        Assert.Equal(ExtensionUri, (await call.ResponseHeadersAsync).GetValue(MetadataKey));
    }

    [Fact]
    public async Task MalformedPayload_AfterNegotiationPreservesActivation()
    {
        await using var host = await Host.StartAsync(onInvoke: _ => Assert.Fail("The handler must not run."));
        using var call = host.Client.InvokeExtensionOperationAsync(
            new ExtensionOperationRequest { OperationId = UnaryId, Payload = ByteString.CopyFromUtf8("{") }, ActiveHeaders());

        var error = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);

        Assert.Equal(A2AErrorCode.InvalidParams, GrpcErrorMapping.ToA2AException(error).ErrorCode);
        Assert.Equal(ExtensionUri, (await call.ResponseHeadersAsync).GetValue(MetadataKey));
        Assert.Equal(ExtensionUri, error.Trailers.GetValue(MetadataKey));
    }

    [Fact]
    public async Task RepeatedEquivalentConfiguration_NegotiatesOnlyOnce()
    {
        var calls = 0;
        await using var host = await Host.StartAsync(duplicateConfiguration: true,
            canActivate: (_, _, _) => { calls++; return ValueTask.FromResult(true); });
        using var call = host.Client.InvokeExtensionOperationAsync(Request(UnaryId), ActiveHeaders());

        await call.ResponseAsync;

        Assert.Equal(1, calls);
        Assert.Equal(ExtensionUri, (await call.ResponseHeadersAsync).GetValue(MetadataKey));
    }

    [Fact]
    public async Task ExistingRegistryRegistration_ActivatesWithoutAPolicy()
    {
        await using var host = await Host.StartAsync(existingRegistration: true);
        using var call = host.Client.InvokeExtensionOperationAsync(Request(UnaryId), ActiveHeaders());

        await call.ResponseAsync;

        Assert.Equal(ExtensionUri, (await call.ResponseHeadersAsync).GetValue(MetadataKey));
    }

    [Fact]
    public async Task HandlerOnlyRegistration_RemainsNegotiationFree()
    {
        await using var host = await Host.StartAsync(negotiationEnabled: false, requiredDebate: true);
        using var call = host.StandardClient.GetTaskAsync(new Protos.GetTaskRequest { Id = "task-42" },
            new Metadata { { MetadataKey, "not an extension identifier" } });

        await call.ResponseAsync;

        Assert.NotNull(host.Handler.LastGetTask);
        Assert.Null((await call.ResponseHeadersAsync).GetValue(MetadataKey));
    }

    private static Metadata ActiveHeaders() => new() { { MetadataKey, ExtensionUri } };

    private static ExtensionOperationRequest Request(string operationId) => new()
    {
        OperationId = operationId,
        Payload = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(new GrpcCustomRequest("debate-42"), GrpcCustomJsonContext.Default.GrpcCustomRequest)),
    };

    private static Protos.SendMessageRequest StandardMessage() => new()
    {
        Message = new Protos.Message { MessageId = "client-1", Role = Protos.Role.User, Parts = { new Protos.Part { Text = "hello" } } },
    };

    private sealed class Host(WebApplication app, GrpcChannel channel, A2ACustomOperationRegistry registry,
        FakeRequestHandler handler) : IAsyncDisposable
    {
        public A2ACustomOperationRegistry Registry { get; } = registry;
        public FakeRequestHandler Handler { get; } = handler;
        public A2AExtensionService.A2AExtensionServiceClient Client { get; } = new(channel);
        public Protos.A2AService.A2AServiceClient StandardClient { get; } = new(channel);

        public static async Task<Host> StartAsync(
            Func<ServerCallContext, A2AExtensionRegistration, CancellationToken, ValueTask<bool>>? canActivate = null,
            Action<A2ACustomOperationContext>? onInvoke = null,
            bool requiredDebate = false, bool requiredOther = false,
            bool duplicateConfiguration = false, bool existingRegistration = false, bool negotiationEnabled = true,
            Func<A2ACustomOperationContext, GrpcCustomRequest, CancellationToken, IAsyncEnumerable<GrpcCustomResult>>? streamFactory = null)
        {
            var builder = new A2ACustomOperationRegistryBuilder();
            var debate = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri, Required = requiredDebate });
            if (requiredOther) builder.RegisterExtension(new AgentExtension { Uri = OtherUri, Required = true });
            builder.Map<GrpcCustomRequest, GrpcCustomResult>(debate, new A2AOperationId(UnaryId),
                (context, request, _) => { onInvoke?.Invoke(context); return ValueTask.FromResult(new GrpcCustomResult(request.Value)); },
                GrpcCustomJsonContext.Default.GrpcCustomRequest, GrpcCustomJsonContext.Default.GrpcCustomResult);
            builder.MapStreaming<GrpcCustomRequest, GrpcCustomResult>(debate, new A2AOperationId(StreamId),
                (context, request, ct) => streamFactory is null ? Stream(context, request, onInvoke, ct) : streamFactory(context, request, ct),
                GrpcCustomJsonContext.Default.GrpcCustomRequest, GrpcCustomJsonContext.Default.GrpcCustomResult);
            var registry = builder.Build();
            var handler = new FakeRequestHandler();
            handler.StreamEvents.Add(new StreamResponse { Message = new Message { MessageId = "agent-1", Role = Role.Agent, Parts = [Part.FromText("reply")] } });
            var appBuilder = WebApplication.CreateBuilder();
            appBuilder.WebHost.UseTestServer();
            appBuilder.Services.AddSingleton<IA2ARequestHandler>(handler);
            if (!negotiationEnabled)
            {
                appBuilder.Services.AddA2AGrpc();
                appBuilder.Services.AddSingleton(registry);
            }
            else if (existingRegistration) appBuilder.Services.AddA2AGrpcCustomOperations(registry);
            else
            {
                var options = new A2AGrpcExtensionOptions(registry) { CanActivate = canActivate };
                var grpc = appBuilder.Services.AddA2AGrpc(options);
                if (duplicateConfiguration) grpc.WithA2AExtensions<A2AGrpcCustomOperationService>(options);
            }
            var app = appBuilder.Build();
            app.MapGrpcA2A();
            app.MapGrpcA2ACustomOperations();
            await app.StartAsync();
            var testServer = app.GetTestServer();
            var channel = GrpcChannel.ForAddress(testServer.BaseAddress, new GrpcChannelOptions { HttpHandler = testServer.CreateHandler() });
            return new Host(app, channel, registry, handler);
        }

        private static async IAsyncEnumerable<GrpcCustomResult> Stream(A2ACustomOperationContext context, GrpcCustomRequest request,
            Action<A2ACustomOperationContext>? onInvoke, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onInvoke?.Invoke(context);
            yield return new GrpcCustomResult(request.Value);
            await Task.Yield();
        }

        public async ValueTask DisposeAsync()
        {
            channel.Dispose();
            await app.DisposeAsync();
        }
    }
}

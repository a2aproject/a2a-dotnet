namespace A2A.Grpc.UnitTests;

using A2A.AspNetCore;
using A2A.Grpc.AspNetCore;
using global::Grpc.Core;
using global::Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

public sealed class StandardExtensionContextTests
{
    private const string ExtensionUri = "https://example.org/extensions/usage/v1";

    [Theory]
    [InlineData("rpc", false, false)]
    [InlineData("rpc", false, true)]
    [InlineData("rpc", true, false)]
    [InlineData("rpc", true, true)]
    [InlineData("http", false, false)]
    [InlineData("http", false, true)]
    [InlineData("http", true, false)]
    [InlineData("http", true, true)]
    [InlineData("grpc", false, false)]
    [InlineData("grpc", false, true)]
    [InlineData("grpc", true, false)]
    [InlineData("grpc", true, true)]
    public async Task StandardAgent_ConsumesTheSameTransportNeutralContext(string transport, bool streaming, bool requested)
    {
        await using var host = await Host.StartAsync();

        var body = await host.SendAsync(transport, streaming, requested, "m1");

        var activation = Assert.IsType<A2ARequestContext>(host.Agent.Contexts["m1"].ExtensionActivation);
        Assert.Equal(requested, activation.RequestedExtensions.Contains(ExtensionUri));
        Assert.Equal(requested, activation.ActivatedExtensions.Contains(ExtensionUri));
        Assert.Equal(requested ? 1 : 0, host.PolicyCalls);
        Assert.Contains(requested ? "activated" : "not-requested", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rpc")]
    [InlineData("http")]
    [InlineData("grpc")]
    public async Task ConcurrentRequests_KeepTheirOwnAgentSnapshots(string transport)
    {
        await using var host = await Host.StartAsync();

        await Task.WhenAll(Enumerable.Range(0, 12).Select(index =>
            host.SendAsync(transport, index % 3 == 0, index % 2 == 0, $"m{index}")));

        var snapshots = new HashSet<A2ARequestContext>();
        for (var index = 0; index < 12; index++)
        {
            var activation = Assert.IsType<A2ARequestContext>(host.Agent.Contexts[$"m{index}"].ExtensionActivation);
            Assert.Equal(index % 2 == 0, activation.ActivatedExtensions.Contains(ExtensionUri));
            Assert.True(snapshots.Add(activation));
        }
        Assert.Equal(6, host.PolicyCalls);
    }

    [Theory]
    [InlineData("rpc")]
    [InlineData("http")]
    [InlineData("grpc")]
    public async Task AvailabilityChange_DoesNotChangeTheRunningAgentSnapshot(string transport)
    {
        await using var host = await Host.StartAsync();
        host.Agent.AfterFirstReply = () => host.Available = false;

        var stream = await host.SendAsync(transport, streaming: true, requested: true, "stream");
        host.Agent.AfterFirstReply = null;
        var next = await host.SendAsync(transport, streaming: false, requested: true, "next");

        Assert.Contains("activated", stream, StringComparison.Ordinal);
        Assert.DoesNotContain("declined", stream, StringComparison.Ordinal);
        Assert.Contains("declined", next, StringComparison.Ordinal);
        Assert.Contains(ExtensionUri, host.Agent.Contexts["stream"].ExtensionActivation!.ActivatedExtensions);
        Assert.Empty(host.Agent.Contexts["next"].ExtensionActivation!.ActivatedExtensions);
        Assert.Contains(ExtensionUri, host.Agent.Contexts["next"].ExtensionActivation!.RequestedExtensions);
        Assert.Equal(2, host.PolicyCalls);
    }

    [Theory]
    [InlineData("rpc", false)]
    [InlineData("rpc", true)]
    [InlineData("http", false)]
    [InlineData("http", true)]
    [InlineData("grpc", false)]
    [InlineData("grpc", true)]
    public async Task HandlerOnlyHosting_DoesNotInventANegotiatedContext(string transport, bool streaming)
    {
        await using var host = await Host.StartAsync(negotiationEnabled: false);

        var body = await host.SendAsync(transport, streaming, requested: true, "legacy");

        Assert.Null(host.Agent.Contexts["legacy"].ExtensionActivation);
        Assert.Contains("unnegotiated", body, StringComparison.Ordinal);
        Assert.Equal(0, host.PolicyCalls);
    }

    private sealed class RecordingAgent : IAgentHandler
    {
        public ConcurrentDictionary<string, RequestContext> Contexts { get; } = new();
        public Action? AfterFirstReply { get; set; }

        public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            Contexts[context.Message.MessageId] = context;
            var snapshot = context.ExtensionActivation;
            await Task.Yield();
            await ReplyAsync(context, eventQueue, snapshot, cancellationToken);
            if (context.StreamingResponse)
            {
                AfterFirstReply?.Invoke();
                Assert.Same(snapshot, context.ExtensionActivation);
                await ReplyAsync(context, eventQueue, snapshot, cancellationToken);
            }
            eventQueue.Complete();
        }

        private static async Task ReplyAsync(RequestContext context, AgentEventQueue queue,
            A2ARequestContext? activation, CancellationToken cancellationToken)
        {
            var text = activation is null ? "unnegotiated"
                : activation.ActivatedExtensions.Contains(ExtensionUri) ? "activated"
                : activation.RequestedExtensions.Contains(ExtensionUri) ? "declined" : "not-requested";
            await queue.EnqueueMessageAsync(new Message
            {
                MessageId = Guid.NewGuid().ToString("N"),
                ContextId = context.ContextId,
                Role = Role.Agent,
                Parts = [Part.FromText(text)],
            }, cancellationToken);
        }
    }

    private sealed class Host(WebApplication app, HttpClient http, GrpcChannel channel,
        A2AServer server, RecordingAgent agent, Availability availability) : IAsyncDisposable
    {
        public RecordingAgent Agent { get; } = agent;
        public bool Available { set => Volatile.Write(ref availability.Available, value ? 1 : 0); }
        public int PolicyCalls => Volatile.Read(ref availability.Calls);

        public static async Task<Host> StartAsync(bool negotiationEnabled = true)
        {
            var registryBuilder = new A2ACustomOperationRegistryBuilder();
            registryBuilder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
            var registry = registryBuilder.Build();
            var availability = new Availability();
            var agent = new RecordingAgent();
            var server = new A2AServer(agent, new InMemoryTaskStore(), new ChannelEventNotifier(),
                NullLogger<A2AServer>.Instance);
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IA2ARequestHandler>(server);
            builder.Services.ConfigureHttpJsonOptions(options =>
                options.SerializerOptions.TypeInfoResolverChain.Add(A2AJsonUtilities.DefaultOptions.TypeInfoResolver!));
            if (negotiationEnabled)
            {
                builder.Services.AddA2AGrpc(new A2AGrpcExtensionOptions(registry)
                {
                    CanActivate = (_, _, _) => availability.Evaluate(),
                });
            }
            else builder.Services.AddA2AGrpc();
            var app = builder.Build();
            if (negotiationEnabled)
            {
                var options = new A2AExtensionOptions(registry) { CanActivate = (_, _, _) => availability.Evaluate() };
                app.MapA2A(server, "/rpc", options);
                app.MapHttpA2A(server, "/http", options);
            }
            else
            {
                app.MapA2A(server, "/rpc");
                app.MapHttpA2A(server, "/http");
            }
            app.MapGrpcA2A();
            await app.StartAsync();
            var testServer = app.GetTestServer();
            var http = testServer.CreateClient();
            var channel = GrpcChannel.ForAddress(testServer.BaseAddress,
                new GrpcChannelOptions { HttpHandler = testServer.CreateHandler() });
            return new Host(app, http, channel, server, agent, availability);
        }

        public async Task<string> SendAsync(string transport, bool streaming, bool requested, string messageId)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var request = new SendMessageRequest
            {
                Message = new Message { MessageId = messageId, Role = Role.User, Parts = [Part.FromText("hello")] },
            };
            if (transport == "grpc")
            {
                var client = new Protos.A2AService.A2AServiceClient(channel);
                var metadata = new Metadata { { "a2a-version", "1.0" } };
                if (requested) metadata.Add("a2a-extensions", ExtensionUri);
                if (streaming)
                {
                    using var call = client.SendStreamingMessage(ProtoMap.ToProto(request), metadata, cancellationToken: timeout.Token);
                    var texts = new List<string>();
                    while (await call.ResponseStream.MoveNext(timeout.Token))
                        texts.Add(call.ResponseStream.Current.Message.Parts[0].Text);
                    return string.Join(";", texts);
                }
                using var unary = client.SendMessageAsync(ProtoMap.ToProto(request), metadata, cancellationToken: timeout.Token);
                return (await unary.ResponseAsync).Message.Parts[0].Text;
            }

            var url = transport == "rpc" ? "/rpc" : streaming ? "/http/message:stream" : "/http/message:send";
            var payload = transport == "rpc"
                ? JsonSerializer.Serialize(new JsonRpcRequest
                {
                    Id = new JsonRpcId(messageId),
                    Method = streaming ? A2AMethods.SendStreamingMessage : A2AMethods.SendMessage,
                    Params = JsonSerializer.SerializeToElement(request, A2AJsonUtilities.DefaultOptions),
                }, A2AJsonUtilities.DefaultOptions)
                : JsonSerializer.Serialize(request, A2AJsonUtilities.DefaultOptions);
            using var message = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            message.Headers.Add("A2A-Version", "1.0");
            if (requested) message.Headers.Add("A2A-Extensions", ExtensionUri);
            using var response = await http.SendAsync(message, timeout.Token);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            Assert.DoesNotContain("\"error\"", body, StringComparison.Ordinal);
            return body;
        }

        public async ValueTask DisposeAsync()
        {
            http.Dispose();
            channel.Dispose();
            await server.DisposeAsync();
            await app.DisposeAsync();
        }
    }

    private sealed class Availability
    {
        internal int Available = 1;
        internal int Calls;
        internal ValueTask<bool> Evaluate()
        {
            Interlocked.Increment(ref Calls);
            return ValueTask.FromResult(Volatile.Read(ref Available) == 1);
        }
    }
}

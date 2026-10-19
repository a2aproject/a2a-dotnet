namespace A2A.Itk.UnitTests;

using A2A;
using A2A.Itk.Proto;
using A2A.V0_3Compat;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using System.Text.Json;

public sealed class ItkAgentTests
{
    [Fact]
    public void GetAgentCard_AdvertisesPushNotifications()
    {
        var original = Environment.GetEnvironmentVariable("ITK_ACTS_REDUCED_CAPABILITIES");
        Environment.SetEnvironmentVariable("ITK_ACTS_REDUCED_CAPABILITIES", null);

        try
        {
            var card = ItkAgent.GetAgentCard(10102);

            Assert.True(card.Capabilities.PushNotifications);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ITK_ACTS_REDUCED_CAPABILITIES", original);
        }
    }

    [Fact]
    public void GetAgentCard_DisablesPushNotificationsInReducedMode()
    {
        var original = Environment.GetEnvironmentVariable("ITK_ACTS_REDUCED_CAPABILITIES");
        Environment.SetEnvironmentVariable("ITK_ACTS_REDUCED_CAPABILITIES", "1");

        try
        {
            var card = ItkAgent.GetAgentCard(10102);

            Assert.False(card.Capabilities.PushNotifications);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ITK_ACTS_REDUCED_CAPABILITIES", original);
        }
    }

    [Fact]
    public void GetAgentCard_AdvertisesGrpcInterface()
    {
        var card = ItkAgent.GetAgentCard(10102);

        var grpcInterface = Assert.Single(
            card.SupportedInterfaces,
            agentInterface => agentInterface.ProtocolBinding == ProtocolBindingNames.Grpc);

        Assert.Equal("127.0.0.1:11002", grpcInterface.Url);
        Assert.Equal("1.0", grpcInterface.ProtocolVersion);
    }

    [Fact]
    public void GetAgentCard_UsesConfiguredGrpcPort()
    {
        var card = ItkAgent.GetAgentCard(20102, 21002);

        var grpcInterface = Assert.Single(
            card.SupportedInterfaces,
            agentInterface => agentInterface.ProtocolBinding == ProtocolBindingNames.Grpc);

        Assert.Equal("127.0.0.1:21002", grpcInterface.Url);
    }

    [Fact]
    public async Task ActsServer_EnablesPushNotificationOperations()
    {
        await using var server = CreateActsServer();

        var exception = await Assert.ThrowsAsync<A2AException>(
            () => server.CreateTaskPushNotificationConfigAsync(new()
            {
                TaskId = "missing-task",
                Url = "http://127.0.0.1:1234/notifications",
            }, null));

        Assert.Equal(A2AErrorCode.TaskNotFound, exception.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(LegacyPushRequests))]
    public async Task ActsServer_RejectsV03PushRegistrations(string requestJson)
    {
        await using var server = CreateActsServer();
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Request.ContentType = "application/json";
        context.Request.Headers["A2A-Version"] = "0.3";

        var result = await V03ServerProcessor.ProcessRequestAsync(
            server,
            context.Request,
            CancellationToken.None);

        var json = await ExecuteResultAsync(result);
        Assert.StartsWith("{", json);
        using var body = JsonDocument.Parse(json);
        Assert.True(body.RootElement.TryGetProperty("error", out var error));
        Assert.Equal(
            (int)A2AErrorCode.PushNotificationNotSupported,
            error.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task ActsServer_DiscoveryAdvertisesPushOnlyForV1()
    {
        var card = CreatePushEnabledCard();
        await using var server = CreateActsServer(card);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapAgentCardGetWithV03Compat(server, () => Task.FromResult(card));
        app.MapAgentCardGetWithV03Compat(server, () => Task.FromResult(card), "/jsonrpc");
        await app.StartAsync();
        using var client = app.GetTestClient();

        foreach (var path in new[]
        {
            "/.well-known/agent-card.json",
            "/jsonrpc/.well-known/agent-card.json",
        })
        {
            foreach (var (version, expected) in new[]
            {
                ((string?)null, false),
                ("0.3", false),
                ("1.0", true),
            })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                if (version is not null)
                {
                    request.Headers.Add("A2A-Version", version);
                }

                using var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(
                    expected,
                    body.RootElement.GetProperty("capabilities").GetProperty("pushNotifications").GetBoolean());
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotSendGrpcInstructionToV03PeerOverJsonRpc()
    {
        var handler = new V03CardHandler();
        var agent = new ItkAgent(
            new TestHttpClientFactory(new HttpClient(handler)),
            NullLogger<ItkAgent>.Instance);
        var instruction = new Instruction
        {
            CallAgent = new CallAgent
            {
                Transport = "GRPC",
                AgentCardUri = "http://peer",
                Instruction = new Instruction
                {
                    ReturnResponse = new ReturnResponse { Response = "done" },
                },
                SendMessage = new SendMessageBehavior(),
            },
        };
        var context = new RequestContext
        {
            Message = new Message
            {
                MessageId = "message-1",
                Role = Role.User,
                Parts = [Part.FromRaw(instruction.ToByteArray(), "application/x-protobuf")],
            },
            TaskId = "task-1",
            ContextId = "context-1",
            StreamingResponse = false,
        };
        var eventQueue = new AgentEventQueue();

        await agent.ExecuteAsync(context, eventQueue, CancellationToken.None);
        eventQueue.Complete();

        Assert.Equal(0, handler.JsonRpcRequestCount);

        var events = new List<StreamResponse>();
        await foreach (var response in eventQueue)
        {
            events.Add(response);
        }

        var finalUpdate = Assert.IsType<TaskStatusUpdateEvent>(events[^1].StatusUpdate);
        Assert.Equal(TaskState.Failed, finalUpdate.Status.State);
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    public static TheoryData<string> LegacyPushRequests => new()
    {
        """{"jsonrpc":"2.0","method":"tasks/pushNotificationConfig/set","id":1,"params":{"taskId":"missing-task","pushNotificationConfig":{"id":"cfg","url":"http://127.0.0.1:1234/notifications"}}}""",
        """{"jsonrpc":"2.0","method":"message/send","id":2,"params":{"message":{"kind":"message","messageId":"m1","role":"user","parts":[{"kind":"text","text":"hi"}]},"configuration":{"acceptedOutputModes":[],"pushNotificationConfig":{"url":"http://127.0.0.1:1234/notifications"}}}}""",
        """{"jsonrpc":"2.0","method":"message/stream","id":3,"params":{"message":{"kind":"message","messageId":"m1","role":"user","parts":[{"kind":"text","text":"hi"}]},"configuration":{"acceptedOutputModes":[],"pushNotificationConfig":{"url":"http://127.0.0.1:1234/notifications"}}}}""",
    };

    private static ActsServer CreateActsServer(AgentCard? card = null)
    {
        card ??= CreatePushEnabledCard();
        return new ActsServer(
            new CompletingAgent(),
            new InMemoryTaskStore(),
            new ChannelEventNotifier(),
            NullLogger<A2AServer>.Instance,
            new A2AServerOptions(),
            card,
            new InMemoryPushNotificationStore(),
            CreateItkValidator(),
            new NoOpPushNotificationSender(),
            ActsServer.Extend(card));
    }

    private static AgentCard CreatePushEnabledCard() => new()
    {
        Capabilities = new AgentCapabilities { PushNotifications = true },
        DefaultInputModes = [],
        DefaultOutputModes = [],
        Skills = [],
        SupportedInterfaces = [],
    };

    private static async Task<string> ExecuteResultAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;
        await result.ExecuteAsync(context);
        body.Position = 0;
        using var reader = new StreamReader(body);
        return await reader.ReadToEndAsync();
    }

    private static IPushNotificationUrlValidator CreateItkValidator()
    {
        var validatorType = typeof(ItkAgent).Assembly.GetType(
            "A2A.Itk.ItkPushNotificationUrlValidator");

        Assert.NotNull(validatorType);
        return Assert.IsAssignableFrom<IPushNotificationUrlValidator>(
            Activator.CreateInstance(validatorType, nonPublic: true));
    }

    private sealed class CompletingAgent : IAgentHandler
    {
        public async Task ExecuteAsync(
            RequestContext context,
            AgentEventQueue eventQueue,
            CancellationToken cancellationToken)
        {
            await eventQueue.EnqueueTaskAsync(new AgentTask
            {
                Id = context.TaskId,
                ContextId = context.ContextId,
                Status = new TaskStatus { State = TaskState.Completed },
                History = [context.Message],
            }, cancellationToken);
            eventQueue.Complete();
        }
    }

    private sealed class NoOpPushNotificationSender : IPushNotificationSender
    {
        public Task EnqueueAsync(
            PushNotificationConfigSnapshot registration,
            StreamResponse response,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class V03CardHandler : HttpMessageHandler
    {
        public int JsonRpcRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"url":"http://peer/jsonrpc"}"""),
                });
            }

            JsonRpcRequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}

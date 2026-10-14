namespace A2A.Itk.UnitTests;

using A2A;
using A2A.Itk.Proto;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

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
        var card = new AgentCard
        {
            Capabilities = new AgentCapabilities { PushNotifications = true },
            DefaultInputModes = [],
            DefaultOutputModes = [],
            Skills = [],
            SupportedInterfaces = [],
        };
        var constructor = typeof(ActsServer).GetConstructor(
        [
            typeof(IAgentHandler),
            typeof(ITaskStore),
            typeof(ChannelEventNotifier),
            typeof(Microsoft.Extensions.Logging.ILogger<A2AServer>),
            typeof(A2AServerOptions),
            typeof(AgentCard),
            typeof(IPushNotificationStore),
            typeof(IPushNotificationUrlValidator),
            typeof(IPushNotificationSender),
            typeof(AgentCard),
        ]);

        Assert.NotNull(constructor);
        var server = Assert.IsAssignableFrom<IA2ARequestHandler>(constructor.Invoke(
        [
            new NoOpAgent(),
            new InMemoryTaskStore(),
            new ChannelEventNotifier(),
            NullLogger<A2AServer>.Instance,
            new A2AServerOptions(),
            card,
            new InMemoryPushNotificationStore(),
            CreateItkValidator(),
            new NoOpPushNotificationSender(),
            ActsServer.Extend(card),
        ]));

        var exception = await Assert.ThrowsAsync<A2AException>(
            () => server.CreateTaskPushNotificationConfigAsync(new()
            {
                TaskId = "missing-task",
                Url = "http://127.0.0.1:1234/notifications",
            }));

        Assert.Equal(A2AErrorCode.TaskNotFound, exception.ErrorCode);
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

    private static IPushNotificationUrlValidator CreateItkValidator()
    {
        var validatorType = typeof(ItkAgent).Assembly.GetType(
            "A2A.Itk.ItkPushNotificationUrlValidator");

        Assert.NotNull(validatorType);
        return Assert.IsAssignableFrom<IPushNotificationUrlValidator>(
            Activator.CreateInstance(validatorType, nonPublic: true));
    }

    private sealed class NoOpAgent : IAgentHandler
    {
        public Task ExecuteAsync(
            RequestContext context,
            AgentEventQueue eventQueue,
            CancellationToken cancellationToken) => Task.CompletedTask;
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

namespace A2A.Itk.UnitTests;

using A2A;
using A2A.Itk.Proto;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

public sealed class ItkAgentTests
{
    [Fact]
    public void GetAgentCard_AdvertisesGrpcInterface()
    {
        var card = ItkAgent.GetAgentCard(10102);

        var grpcInterface = Assert.Single(
            card.SupportedInterfaces,
            agentInterface => agentInterface.ProtocolBinding == ProtocolBindingNames.Grpc);

        Assert.Equal("http://127.0.0.1:11002", grpcInterface.Url);
        Assert.Equal("1.0", grpcInterface.ProtocolVersion);
    }

    [Fact]
    public void GetAgentCard_UsesConfiguredGrpcPort()
    {
        var card = ItkAgent.GetAgentCard(20102, 21002);

        var grpcInterface = Assert.Single(
            card.SupportedInterfaces,
            agentInterface => agentInterface.ProtocolBinding == ProtocolBindingNames.Grpc);

        Assert.Equal("http://127.0.0.1:21002", grpcInterface.Url);
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

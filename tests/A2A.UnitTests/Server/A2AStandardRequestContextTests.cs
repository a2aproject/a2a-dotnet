using Microsoft.Extensions.Logging.Abstractions;

namespace A2A.UnitTests.Server;

public sealed class A2AStandardRequestContextTests
{
    private const string ExtensionUri = "https://example.org/extensions/usage/v1";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Exercise the public handler contract, not concrete server overloads.")]
    public async Task HandlerContract_PropagatesExplicitContextWithoutAnAdapter(bool streaming)
    {
        var agent = new Agent();
        await using var server = CreateServer(agent);
        IA2ARequestHandler handler = server;
        var activation = await CreateActivationAsync();

        if (streaming)
        {
            await foreach (var _ in handler.SendStreamingMessageAsync(MessageRequest(), activation, CancellationToken.None)) { }
        }
        else
        {
            await handler.SendMessageAsync(MessageRequest(), activation, CancellationToken.None);
        }

        Assert.Same(activation, agent.Context!.ExtensionActivation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Server_PropagatesSnapshotToAgentWithoutLeakingIntoLaterUnnegotiatedCalls(bool streaming)
    {
        var agent = new Agent();
        await using var server = CreateServer(agent);
        var activation = await CreateActivationAsync();
        if (streaming)
        {
            await foreach (var _ in server.SendStreamingMessageAsync(MessageRequest(), activation)) { }
        }
        else await server.SendMessageAsync(MessageRequest(), activation);
        Assert.Same(activation, agent.Context!.ExtensionActivation);

        await server.SendMessageAsync(MessageRequest(), null, default);
        Assert.Null(agent.Context!.ExtensionActivation);
    }

    [Fact]
    public async Task CancelTask_UsesTheCancellingRequestsSnapshotRatherThanStoredTaskState()
    {
        var agent = new Agent();
        var store = new InMemoryTaskStore();
        await store.SaveTaskAsync("t", new AgentTask
        {
            Id = "t",
            ContextId = "c",
            Status = new TaskStatus { State = TaskState.Working },
        });
        await using var server = CreateServer(agent, store);
        var activation = await CreateActivationAsync();

        var task = await server.CancelTaskAsync(new CancelTaskRequest { Id = "t" }, activation);

        Assert.Equal(TaskState.Canceled, task.Status.State);
        Assert.Same(activation, agent.Context!.ExtensionActivation);
    }

    [Fact]
    public async Task DetachedExecution_RetainsTheInitiatingSnapshotAfterTheResponseReturns()
    {
        var agent = new Agent { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var server = CreateServer(agent);
        var activation = await CreateActivationAsync();
        var request = MessageRequest();
        request.Configuration = new SendMessageConfiguration { ReturnImmediately = true };
        try
        {
            var response = await server.SendMessageAsync(request, activation)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(response.Task);
            Assert.Same(activation, agent.Context!.ExtensionActivation);
            Assert.False(agent.Gate.Task.IsCompleted);
        }
        finally
        {
            agent.Gate.TrySetResult();
        }
    }

    private static A2AServer CreateServer(Agent agent, InMemoryTaskStore? store = null)
        => new(agent, store ?? new InMemoryTaskStore(), new ChannelEventNotifier(), NullLogger<A2AServer>.Instance);

    private static SendMessageRequest MessageRequest() => new()
    {
        Message = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.User, Parts = [Part.FromText("hello")] },
    };

    private static async Task<A2ARequestContext> CreateActivationAsync(bool active = true)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        return await builder.Build().NegotiateExtensionsAsync(active ? [ExtensionUri] : []);
    }

    private sealed class Agent : IAgentHandler
    {
        public RequestContext? Context { get; private set; }
        public TaskCompletionSource? Gate { get; init; }

        public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            Context = context;
            var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
            await updater.SubmitAsync(cancellationToken: cancellationToken);
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }
            await updater.CompleteAsync(cancellationToken: cancellationToken);
        }

        public async Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            Context = context;
            await new TaskUpdater(eventQueue, context.TaskId, context.ContextId).CancelAsync(cancellationToken: cancellationToken);
        }
    }
}

using Microsoft.Extensions.Logging.Abstractions;

namespace A2A.UnitTests.Server;

public sealed class A2AStandardRequestContextTests
{
    private const string ExtensionUri = "https://example.org/extensions/usage/v1";

    [Fact]
    public async Task ContextAdapter_DefaultsPreserveAllLegacyOperationsAndCancellationTokens()
    {
        var handler = new ContextualHandler();
        var activation = await CreateActivationAsync();
        var bound = handler.WithRequestContext(activation);
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        await bound.SendMessageAsync(MessageRequest(), token);
        await foreach (var _ in bound.SendStreamingMessageAsync(MessageRequest(), token)) { }
        await bound.GetTaskAsync(new GetTaskRequest { Id = "t" }, token);
        await bound.ListTasksAsync(new ListTasksRequest(), token);
        await bound.CancelTaskAsync(new CancelTaskRequest { Id = "t" }, token);
        await foreach (var _ in bound.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "t" }, token)) { }
        await bound.CreateTaskPushNotificationConfigAsync(new TaskPushNotificationConfig { TaskId = "t", Url = "https://example.org/hook" }, token);
        await bound.GetTaskPushNotificationConfigAsync(new GetTaskPushNotificationConfigRequest { TaskId = "t", Id = "c" }, token);
        await bound.ListTaskPushNotificationConfigsAsync(new ListTaskPushNotificationConfigsRequest { TaskId = "t" }, token);
        await bound.DeleteTaskPushNotificationConfigAsync(new DeleteTaskPushNotificationConfigRequest { TaskId = "t", Id = "c" }, token);
        await bound.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest(), token);

        Assert.Equal(11, handler.Calls.Count);
        Assert.All(handler.Calls, call => Assert.Equal(token, call));
        Assert.Same(activation, handler.SeenContext);
    }

    [Fact]
    public async Task BindingAndRebinding_DoNotMutateTheSharedHandlerOrExistingAdapters()
    {
        var handler = new ContextualHandler();
        var active = await CreateActivationAsync();
        var inactive = await CreateActivationAsync(active: false);
        var first = handler.WithRequestContext(active);
        var second = first.WithRequestContext(inactive);

        await second.GetTaskAsync(new GetTaskRequest { Id = "t" });
        Assert.Same(inactive, handler.SeenContext);
        await first.GetTaskAsync(new GetTaskRequest { Id = "t" });
        Assert.Same(active, handler.SeenContext);
        Assert.Same(handler, first.WithRequestContext(null));
        Assert.Same(handler, handler.WithRequestContext(null));
        var legacy = new LegacyHandler();
        Assert.Same(legacy, legacy.WithRequestContext(active));
        Assert.Throws<ArgumentNullException>(() => A2ARequestHandlerExtensions.WithRequestContext(null!, active));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Server_PropagatesSnapshotToAgentWithoutLeakingIntoLaterUnnegotiatedCalls(bool streaming)
    {
        var agent = new Agent();
        await using var server = CreateServer(agent);
        var activation = await CreateActivationAsync();
        var bound = server.WithRequestContext(activation);

        if (streaming)
        {
            await foreach (var _ in bound.SendStreamingMessageAsync(MessageRequest())) { }
        }
        else await bound.SendMessageAsync(MessageRequest());
        Assert.Same(activation, agent.Context!.ExtensionActivation);

        // This existing two-argument call must remain source-compatible and context-free.
        await server.SendMessageAsync(MessageRequest(), default);
        Assert.Null(agent.Context!.ExtensionActivation);
    }

    [Fact]
    public async Task CancelTask_UsesTheCancellingRequestsSnapshotRatherThanStoredTaskState()
    {
        var agent = new Agent();
        var store = new InMemoryTaskStore();
        await store.SaveTaskAsync("t", new AgentTask
        {
            Id = "t", ContextId = "c", Status = new TaskStatus { State = TaskState.Working },
        });
        await using var server = CreateServer(agent, store);
        var activation = await CreateActivationAsync();

        var task = await server.WithRequestContext(activation).CancelTaskAsync(new CancelTaskRequest { Id = "t" });

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
            var response = await server.WithRequestContext(activation).SendMessageAsync(request)
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

    private sealed class ContextualHandler : LegacyHandler, IA2ARequestHandlerWithContext
    {
        public A2ARequestContext? SeenContext { get; private set; }
        public Task<AgentTask> GetTaskAsync(GetTaskRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken)
        {
            SeenContext = requestContext;
            return base.GetTaskAsync(request, cancellationToken);
        }
    }

    private class LegacyHandler : IA2ARequestHandler
    {
        public List<CancellationToken> Calls { get; } = [];
        private Task<T> Reply<T>(T value, CancellationToken cancellationToken)
        {
            Calls.Add(cancellationToken);
            return Task.FromResult(value);
        }
        public Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
            => Reply(new SendMessageResponse(), cancellationToken);
        public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
            => Events(cancellationToken);
        public Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken = default)
            => Reply(new AgentTask { Id = "t", ContextId = "c" }, cancellationToken);
        public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
            => Reply(new ListTasksResponse(), cancellationToken);
        public Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken = default)
            => Reply(new AgentTask { Id = "t", ContextId = "c" }, cancellationToken);
        public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken cancellationToken = default)
            => Events(cancellationToken);
        public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(TaskPushNotificationConfig config, CancellationToken cancellationToken = default)
            => Reply(config, cancellationToken);
        public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
            => Reply(new TaskPushNotificationConfig { TaskId = "t", Url = "https://example.org/hook" }, cancellationToken);
        public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken cancellationToken = default)
            => Reply(new ListTaskPushNotificationConfigsResponse(), cancellationToken);
        public async Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
            => await Reply(true, cancellationToken);
        public Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default)
            => Reply(new AgentCard { Name = "test", Description = "test", Version = "1" }, cancellationToken);
        private async IAsyncEnumerable<StreamResponse> Events([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Calls.Add(cancellationToken);
            await Task.Yield();
            yield break;
        }
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace A2A.UnitTests.Server;

public sealed class IndependentPushSemanticsTests
{
    private readonly InMemoryTaskStore _tasks = new();
    private readonly InMemoryPushNotificationStore _configs = new();
    private readonly Handler _handler = new();
    private readonly Sender _sender = new();
    private readonly string _contextId = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task OptionalPushConfig_DoesNotChangeTheHandlersDirectMessageDecision()
    {
        await using var server = Server();
        var request = Request();
        request.Configuration!.TaskPushNotificationConfig = new TaskPushNotificationConfig
        {
            Url = "https://callback.example/notify",
        };
        var response = await server.SendMessageAsync(request);
        Assert.NotNull(response.Message);
        Assert.Null(response.Task);
        Assert.Empty(_sender.Events);
        Assert.Null(await _tasks.GetTaskAsync(_handler.TaskId!));
    }

    [Fact]
    public async Task TaskResponse_HonorsTheNormativeHistoryLimitWithoutTrimmingStorage()
    {
        _handler.ProduceTask = true;
        await using var server = Server();
        var request = Request();
        request.Configuration!.HistoryLength = 0;
        var response = await server.SendMessageAsync(request);
        Assert.NotNull(response.Task);
        Assert.Empty(response.Task.History ?? []);
        Assert.Equal(2, (await _tasks.GetTaskAsync(response.Task.Id))!.History!.Count);
    }

    [Fact]
    public async Task Proto3ZeroPageSize_HasTheSameDefaultMeaningAsOmission()
    {
        await _configs.SaveAsync(new TaskPushNotificationConfig { TaskId = "task", Id = "config", Url = "https://callback.example/" });
        var omitted = await _configs.ListAsync(new() { TaskId = "task" });
        var explicitZero = await _configs.ListAsync(new() { TaskId = "task", PageSize = 0 });
        Assert.Equal(omitted.Configs!.Select(config => config.Id), explicitZero.Configs!.Select(config => config.Id));
        Assert.Equal(omitted.NextPageToken, explicitZero.NextPageToken);
    }

    [Fact]
    public async Task ContinuationStream_BeginsWithTheCurrentTask()
    {
        await _tasks.SaveTaskAsync("task", new AgentTask
        {
            Id = "task",
            ContextId = _contextId,
            Status = new TaskStatus { State = TaskState.InputRequired },
        });
        _handler.StatusOnly = true;
        await using var server = Server();
        var request = Request();
        request.Message.TaskId = "task";
        var events = new List<StreamResponse>();
        await foreach (var response in server.SendStreamingMessageAsync(request))
        {
            events.Add(response);
        }
        Assert.NotNull(events[0].Task);
        Assert.Equal("task", events[0].Task!.Id);
        Assert.Equal(TaskState.Completed, Assert.Single(events, item => item.StatusUpdate is not null).StatusUpdate!.Status.State);
    }

    [Fact]
    public async Task BlockingHandlerEndingWithWorkingTask_IsNotAValidSuccessfulResponse()
    {
        _handler.ProduceTask = true;
        _handler.ResponseState = TaskState.Working;
        await using var server = Server();
        var exception = await Assert.ThrowsAsync<A2AException>(() => server.SendMessageAsync(Request()));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, exception.ErrorCode);
    }

    private A2AServer Server() => new(
        _handler, _tasks, new ChannelEventNotifier(), NullLogger<A2AServer>.Instance, new A2AServerOptions(),
        new AgentCard { Capabilities = new AgentCapabilities { PushNotifications = true } },
        _configs, new Validator(), _sender);

    private SendMessageRequest Request() => new()
    {
        Message = new Message { MessageId = Guid.NewGuid().ToString("N"), ContextId = _contextId, Role = Role.User, Parts = [Part.FromText("request")] },
        Configuration = new SendMessageConfiguration(),
    };

    private sealed class Handler : IAgentHandler
    {
        internal bool ProduceTask { get; set; }
        internal bool StatusOnly { get; set; }
        internal TaskState ResponseState { get; set; } = TaskState.Completed;
        internal string? TaskId { get; private set; }
        public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            TaskId = context.TaskId;
            if (StatusOnly)
            {
                await new TaskUpdater(eventQueue, context.TaskId, context.ContextId).CompleteAsync(cancellationToken: cancellationToken);
                return;
            }
            var message = new Message { MessageId = "reply", Role = Role.Agent, ContextId = context.ContextId, Parts = [Part.FromText("reply")] };
            if (ProduceTask)
            {
                await eventQueue.EnqueueTaskAsync(new AgentTask
                {
                    Id = context.TaskId,
                    ContextId = context.ContextId,
                    Status = new TaskStatus { State = ResponseState },
                    History = [context.Message, message],
                }, cancellationToken);
            }
            else
            {
                await eventQueue.EnqueueMessageAsync(message, cancellationToken);
            }
        }
    }

    private sealed class Validator : IPushNotificationUrlValidator
    {
        public Task<IReadOnlyList<IPAddress>> ValidateAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Loopback]);
    }

    private sealed class Sender : IPushNotificationSender
    {
        internal List<StreamResponse> Events { get; } = [];
        public Task EnqueueAsync(PushNotificationConfigSnapshot registration, StreamResponse response, CancellationToken cancellationToken = default)
        {
            Events.Add(response);
            return Task.CompletedTask;
        }
    }
}

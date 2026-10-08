using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace A2A.UnitTests.Server;

public sealed class PushAuthorizationBoundaryTests
{
    private readonly InMemoryTaskStore _rawTasks = new();
    private readonly InMemoryPushNotificationStore _configs = new();

    [Fact]
    public async Task HostDeniesAnExistingTask_PushOperationsDoNotExposeOrMutateItsConfiguration()
    {
        await _rawTasks.SaveTaskAsync("private", new AgentTask
        {
            Id = "private", ContextId = "context", Status = new TaskStatus { State = TaskState.InputRequired },
        });
        await _configs.SaveAsync(new TaskPushNotificationConfig
        {
            TaskId = "private", Id = "config", Url = "https://callback.example/", Token = "secret",
        });
        var authorizedTasks = new HostAuthorizedTaskStore(_rawTasks);
        await using var server = new A2AServer(new Handler(), authorizedTasks, new ChannelEventNotifier(),
            NullLogger<A2AServer>.Instance, null,
            new AgentCard { Capabilities = new AgentCapabilities { PushNotifications = true } },
            _configs, new Validator(), new Sender());
        var operations = new Func<Task>[]
        {
            () => server.CreateTaskPushNotificationConfigAsync(new() { TaskId = "private", Url = "https://callback.example/" }),
            () => server.GetTaskPushNotificationConfigAsync(new() { TaskId = "private", Id = "config" }),
            () => server.ListTaskPushNotificationConfigsAsync(new() { TaskId = "private" }),
            () => server.DeleteTaskPushNotificationConfigAsync(new() { TaskId = "private", Id = "config" }),
        };
        foreach (var operation in operations)
        {
            var exception = await Assert.ThrowsAsync<A2AException>(operation);
            Assert.Equal(A2AErrorCode.TaskNotFound, exception.ErrorCode);
        }
        Assert.Equal(0, authorizedTasks.RawReads);
        Assert.Equal("secret", (await _configs.GetAsync("private", "config"))!.Configuration.Token);
    }

    private sealed class HostAuthorizedTaskStore(ITaskStore inner) : ITaskStore
    {
        internal int RawReads { get; private set; }
        public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            // This is the host policy seam, not a new SDK authorization system.
            if (taskId == "private")
            {
                return Task.FromResult<AgentTask?>(null);
            }
            RawReads++;
            return inner.GetTaskAsync(taskId, cancellationToken);
        }
        public Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default) =>
            inner.SaveTaskAsync(taskId, task, cancellationToken);
        public Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
            inner.DeleteTaskAsync(taskId, cancellationToken);
        public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Raw listing must not bypass host authorization.");
    }

    private sealed class Handler : IAgentHandler
    {
        public Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Validator : IPushNotificationUrlValidator
    {
        public Task<IReadOnlyList<IPAddress>> ValidateAsync(string url, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unauthorized requests must not reach destination validation.");
    }

    private sealed class Sender : IPushNotificationSender
    {
        public Task EnqueueAsync(PushNotificationConfigSnapshot registration, StreamResponse response, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unauthorized requests must not reach delivery.");
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace A2A.UnitTests.Server;

public sealed partial class TaskContinuationTests
{
    private readonly InMemoryTaskStore _tasks = new();
    private readonly InMemoryPushNotificationStore _registrations = new();
    private readonly ChannelEventNotifier _notifier = new();
    private readonly Sender _sender = new();

    [Theory]
    [InlineData(TaskState.Submitted)]
    [InlineData(TaskState.Working)]
    [InlineData(TaskState.InputRequired)]
    [InlineData(TaskState.AuthRequired)]
    public async Task Continuation_PreservesTaskAndPublishesWithoutAnInboundMessage(TaskState state)
    {
        await SeedAsync(state);
        await _registrations.SaveAsync(Config("first"));
        await _registrations.SaveAsync(Config("second"));
        await using var server = Server();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var subscriber = server.SubscribeToTaskAsync(new() { Id = "task" }, timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        Assert.True(await subscriber.MoveNextAsync());

        var result = await server.ContinueTaskAsync("task", async (context, cancellationToken) =>
        {
            Assert.Equal("task", context.TaskId);
            Assert.Equal("context", context.ContextId);
            Assert.Same(context.Task, context.RequestContext.Task);
            Assert.Null(context.RequestContext.IncomingMessage);
            Assert.Null(context.RequestContext.UserText);
            Assert.Null(context.RequestContext.Configuration);
            Assert.True(context.RequestContext.IsContinuation);
            Assert.Throws<InvalidOperationException>(() => context.RequestContext.Message);
            Assert.Single(context.Task.History!);
            Assert.Single(context.Task.Artifacts!);
            Assert.Equal("original", context.Task.Metadata!["source"].GetString());
            await context.EventQueue.EnqueueMessageAsync(Message("reply"), cancellationToken);
            await context.EventQueue.EnqueueStatusUpdateAsync(new()
            {
                TaskId = context.TaskId, ContextId = context.ContextId, Status = new() { State = TaskState.Completed },
            }, cancellationToken);
        }, timeout.Token);

        Assert.Equal(2, result.PersistedEventCount);
        Assert.Equal(4, result.AcceptedPushNotificationCount);
        Assert.Equal(TaskState.Completed, result.Task.Status.State);
        Assert.Equal(["original", "reply"], result.Task.History!.Select(message => message.MessageId));
        Assert.Single(result.Task.Artifacts!);
        Assert.Equal("original", result.Task.Metadata!["source"].GetString());
        Assert.True(await subscriber.MoveNextAsync());
        Assert.Equal("reply", subscriber.Current.Message!.MessageId);
        Assert.True(await subscriber.MoveNextAsync());
        Assert.Equal(TaskState.Completed, subscriber.Current.StatusUpdate!.Status.State);
        Assert.False(await subscriber.MoveNextAsync());
        Assert.Equal(["reply", "reply", "completed", "completed"], _sender.Events);
        Assert.Equal(TaskState.Completed, (await _tasks.GetTaskAsync("task"))!.Status.State);
    }

    [Theory]
    [InlineData("missing", TaskContinuationFailureKind.TaskNotFound)]
    [InlineData("unregistered", TaskContinuationFailureKind.MissingPushRegistration)]
    [InlineData("terminal", TaskContinuationFailureKind.NonContinuableTask)]
    public async Task Preconditions_FailBeforeProducer(string scenario, TaskContinuationFailureKind expected)
    {
        if (scenario != "missing")
        {
            await SeedAsync(scenario == "terminal" ? TaskState.Completed : TaskState.Working);
        }
        // A registration for another task cannot satisfy the prerequisite.
        var config = Config("other");
        config.TaskId = "another-task";
        await _registrations.SaveAsync(config);
        await using var server = Server();
        bool called = false;
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync(scenario == "missing" ? "missing" : "task", (_, _) =>
            {
                called = true;
                return Task.CompletedTask;
            }));
        Assert.Equal(expected, error.FailureKind);
        Assert.False(called);
        Assert.Empty(_sender.Events);
    }

    [Theory]
    [InlineData(TaskState.Completed)]
    [InlineData(TaskState.Canceled)]
    [InlineData(TaskState.Failed)]
    [InlineData(TaskState.Rejected)]
    public async Task TerminalTask_CannotBeContinued(TaskState state)
    {
        await SeedAsync(state);
        await _registrations.SaveAsync(Config());
        await using var server = Server();
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", (_, _) => throw new InvalidOperationException("must not run")));
        Assert.Equal(TaskContinuationFailureKind.NonContinuableTask, error.FailureKind);
        Assert.Equal(state, (await _tasks.GetTaskAsync("task"))!.Status.State);
    }

    [Theory]
    [InlineData("message", true)]
    [InlineData("message", false)]
    [InlineData("status", true)]
    [InlineData("status", false)]
    [InlineData("artifact", true)]
    [InlineData("artifact", false)]
    [InlineData("task", true)]
    [InlineData("task", false)]
    public async Task MismatchedIdentity_IsRejectedBeforePersistence(string kind, bool wrongTask)
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        await using var server = Server();
        var taskId = wrongTask ? "wrong" : "task";
        var contextId = wrongTask ? "context" : "wrong";
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", async (context, cancellationToken) =>
            {
                var response = kind switch
                {
                    "message" => new StreamResponse { Message = Message("bad", taskId, contextId) },
                    "status" => new StreamResponse
                    {
                        StatusUpdate = new() { TaskId = taskId, ContextId = contextId, Status = new() { State = TaskState.Working } },
                    },
                    "artifact" => new StreamResponse
                    {
                        ArtifactUpdate = new() { TaskId = taskId, ContextId = contextId, Artifact = new() { ArtifactId = "bad", Parts = [] } },
                    },
                    _ => new StreamResponse
                    {
                        Task = new() { Id = taskId, ContextId = contextId, Status = new() { State = TaskState.Working } },
                    },
                };
                await context.EventQueue.WriteAsync(response, cancellationToken);
            }));
        Assert.Equal(TaskContinuationFailureKind.InvalidEvent, error.FailureKind);
        Assert.Equal(0, error.Progress!.PersistedEventCount);
        Assert.Empty(_sender.Events);
        Assert.Single((await _tasks.GetTaskAsync("task"))!.History!);
        Assert.Null(await _tasks.GetTaskAsync("wrong"));
    }

    [Fact]
    public async Task NoEvents_IsNotSuccessful()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        await using var server = Server();
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", (_, _) => Task.CompletedTask));
        Assert.Equal(TaskContinuationFailureKind.NoEvents, error.FailureKind);
        Assert.Equal(0, error.Progress!.PersistedEventCount);
    }

    [Fact]
    public async Task ProducerFailure_ExposesAlreadyCommittedProgress()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        await using var server = Server();
        var failure = new InvalidOperationException("producer failed");
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", async (context, cancellationToken) =>
            {
                await context.EventQueue.EnqueueMessageAsync(Message("committed"), cancellationToken);
                throw failure;
            }));
        Assert.Equal(TaskContinuationFailureKind.Producer, error.FailureKind);
        Assert.Same(failure, error.InnerException);
        Assert.Equal(1, error.Progress!.PersistedEventCount);
        Assert.Equal(1, error.Progress.AcceptedPushNotificationCount);
        Assert.Equal("committed", error.Progress.Task.History![1].MessageId);
        Assert.Equal(2, (await _tasks.GetTaskAsync("task"))!.History!.Count);
    }

    [Fact]
    public async Task AdmissionFailure_ReportsPersistenceAndPartialFanOut()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config("first"));
        await _registrations.SaveAsync(Config("second"));
        _sender.OnEnqueue = (registration, _, _) =>
            registration.Configuration.Id == "second" ? Task.FromException(new IOException("admission")) : Task.CompletedTask;
        await using var server = Server();
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", (context, cancellationToken) =>
                context.EventQueue.EnqueueMessageAsync(Message("committed"), cancellationToken).AsTask()));
        Assert.Equal(TaskContinuationFailureKind.PushAdmission, error.FailureKind);
        Assert.Equal(1, error.Progress!.PersistedEventCount);
        Assert.Equal(1, error.Progress.AcceptedPushNotificationCount);
        Assert.Equal(2, (await _tasks.GetTaskAsync("task"))!.History!.Count);
        Assert.IsType<IOException>(error.InnerException!.InnerException);
    }

    [Fact]
    public async Task RegistrationDeletedByProducer_IsNotCountedAsAccepted()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        await using var server = Server();
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", async (context, cancellationToken) =>
            {
                await server.DeleteTaskPushNotificationConfigAsync(new() { TaskId = "task", Id = "config" }, cancellationToken);
                await context.EventQueue.EnqueueMessageAsync(Message("committed"), cancellationToken);
            }));
        Assert.Equal(TaskContinuationFailureKind.PushAdmission, error.FailureKind);
        Assert.Equal(1, error.Progress!.PersistedEventCount);
        Assert.Equal(0, error.Progress.AcceptedPushNotificationCount);
        Assert.Empty(_sender.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalState_CannotBeReopenedWithinAnExecution(bool fullTask)
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        await using var server = Server();
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", async (context, cancellationToken) =>
            {
                await context.EventQueue.EnqueueStatusUpdateAsync(new()
                {
                    TaskId = context.TaskId, ContextId = context.ContextId, Status = new() { State = TaskState.Completed },
                }, cancellationToken);
                if (fullTask)
                {
                    await context.EventQueue.EnqueueTaskAsync(new()
                    {
                        Id = context.TaskId, ContextId = context.ContextId, Status = new() { State = TaskState.Working },
                    }, cancellationToken);
                }
                else
                {
                    await new TaskUpdater(context.EventQueue, context.TaskId, context.ContextId)
                        .StartWorkAsync(cancellationToken: cancellationToken);
                }
            }));
        Assert.Equal(TaskContinuationFailureKind.NonContinuableTask, error.FailureKind);
        Assert.Equal(1, error.Progress!.PersistedEventCount);
        Assert.Equal(1, error.Progress.AcceptedPushNotificationCount);
        Assert.Equal(TaskState.Completed, (await _tasks.GetTaskAsync("task"))!.Status.State);
    }

    [Fact]
    public async Task TerminalEvent_CanBeFollowedByMessageAndArtifactWithoutChangingTerminalState()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        await using var server = Server();
        var result = await server.ContinueTaskAsync("task", async (context, cancellationToken) =>
        {
            await context.EventQueue.EnqueueStatusUpdateAsync(new()
            {
                TaskId = context.TaskId, ContextId = context.ContextId, Status = new() { State = TaskState.Completed },
            }, cancellationToken);
            await context.EventQueue.EnqueueMessageAsync(Message("after-terminal"), cancellationToken);
            await context.EventQueue.EnqueueArtifactUpdateAsync(new()
            {
                TaskId = "task", ContextId = "context", Artifact = new() { ArtifactId = "late", Parts = [] },
            }, cancellationToken);
        });
        Assert.Equal(3, result.PersistedEventCount);
        Assert.Equal(TaskState.Completed, result.Task.Status.State);
        Assert.Equal(2, result.Task.Artifacts!.Count);
        Assert.Equal("after-terminal", result.Task.History![1].MessageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InboundStreaming_DoesNotPublishRejectedTerminalTransitions(bool fullTask)
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler
        {
            Execute = async (context, queue, cancellationToken) =>
            {
                await release.Task.WaitAsync(cancellationToken);
                await queue.EnqueueStatusUpdateAsync(new()
                {
                    TaskId = context.TaskId, ContextId = context.ContextId, Status = new() { State = TaskState.Completed },
                }, cancellationToken);
                await queue.WriteAsync(fullTask
                    ? new StreamResponse { Task = new() { Id = context.TaskId, ContextId = context.ContextId, Status = new() { State = TaskState.Working } } }
                    : new StreamResponse { StatusUpdate = new() { TaskId = context.TaskId, ContextId = context.ContextId, Status = new() { State = TaskState.Working } } },
                    cancellationToken);
            },
        };
        await using var server = Server(handler);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var stream = server.SendStreamingMessageAsync(new() { Message = Message("inbound") }, timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        try
        {
            Assert.True(await stream.MoveNextAsync());
            Assert.Equal(TaskState.Working, stream.Current.Task!.Status.State);
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(TaskState.Completed, stream.Current.StatusUpdate!.Status.State);
        Assert.False(await stream.MoveNextAsync());
        Assert.Equal(TaskState.Completed, (await _tasks.GetTaskAsync("task"))!.Status.State);
        Assert.Equal(["inbound", "completed"], _sender.Events);
    }

    private A2AServer Server(IAgentHandler? handler = null, ITaskStore? tasks = null, IPushNotificationStore? registrations = null) =>
        new(handler ?? new Handler(), tasks ?? _tasks, _notifier, NullLogger<A2AServer>.Instance, null,
            new AgentCard { Capabilities = new() { PushNotifications = true } },
            registrations ?? _registrations, new Validator(), _sender);

    private Task SeedAsync(TaskState state = TaskState.Working) =>
        _tasks.SaveTaskAsync("task", new()
        {
            Id = "task",
            ContextId = "context",
            Status = new() { State = state },
            History = [Message("original")],
            Artifacts = [new() { ArtifactId = "existing", Parts = [Part.FromText("preserved")] }],
            Metadata = new() { ["source"] = JsonSerializer.SerializeToElement("original") },
        });

    private static Message Message(string id, string taskId = "task", string contextId = "context") =>
        new() { MessageId = id, TaskId = taskId, ContextId = contextId, Role = Role.Agent, Parts = [Part.FromText(id)] };

    private static TaskPushNotificationConfig Config(string id = "config") =>
        new() { Id = id, TaskId = "task", Url = "https://callbacks.example.com/events" };

    private sealed class Handler : IAgentHandler
    {
        public Func<RequestContext, AgentEventQueue, CancellationToken, Task>? Execute { get; init; }
        public Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) =>
            Execute?.Invoke(context, eventQueue, cancellationToken) ?? throw new InvalidOperationException("Inbound handler must not run.");
        public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) =>
            new TaskUpdater(eventQueue, context.TaskId, context.ContextId).CancelAsync(cancellationToken: cancellationToken).AsTask();
    }

    private sealed class Sender : IPushNotificationSender
    {
        public List<string> Events { get; } = [];
        public Func<PushNotificationConfigSnapshot, StreamResponse, CancellationToken, Task>? OnEnqueue { get; set; }
        public async Task EnqueueAsync(PushNotificationConfigSnapshot registration, StreamResponse response, CancellationToken cancellationToken = default)
        {
            if (OnEnqueue is not null)
            {
                await OnEnqueue(registration, response, cancellationToken);
            }
            Events.Add(response.Message?.MessageId ?? response.StatusUpdate?.Status.State.ToString().ToLowerInvariant() ?? "event");
        }
    }

    private sealed class Validator : IPushNotificationUrlValidator
    {
        public Task<IReadOnlyList<System.Net.IPAddress>> ValidateAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<System.Net.IPAddress>>([System.Net.IPAddress.Parse("8.8.8.8")]);
    }
}

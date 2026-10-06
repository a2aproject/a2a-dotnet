using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace A2A.UnitTests.Server;

public sealed class PushNotificationServerTests
{
    private readonly InMemoryTaskStore _tasks = new();
    private readonly InMemoryPushNotificationStore _configs = new();
    private readonly ChannelEventNotifier _notifier = new();
    private readonly RecordingValidator _validator = new();
    private readonly RecordingSender _sender = new();
    private readonly Handler _handler = new();
    private readonly string _tenant = Guid.NewGuid().ToString("N");
    private readonly CaptureLogger _logger = new();

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task DisabledCapability_RejectsEveryOperationAndInlineConfig(bool? enabled)
    {
        await using var server = Server(enabled);
        await ErrorAsync(A2AErrorCode.PushNotificationNotSupported, () => server.CreateTaskPushNotificationConfigAsync(Config()));
        await ErrorAsync(A2AErrorCode.PushNotificationNotSupported, () => server.GetTaskPushNotificationConfigAsync(new() { TaskId = "task", Id = "config" }));
        await ErrorAsync(A2AErrorCode.PushNotificationNotSupported, () => server.ListTaskPushNotificationConfigsAsync(new() { TaskId = "task" }));
        await ErrorAsync(A2AErrorCode.PushNotificationNotSupported, () => server.DeleteTaskPushNotificationConfigAsync(new() { TaskId = "task", Id = "config" }));
        await ErrorAsync(A2AErrorCode.PushNotificationNotSupported, () => server.SendMessageAsync(Request(inline: true)));
        Assert.Equal(0, _handler.Calls);
        Assert.Equal(0, _validator.Calls);
    }

    [Fact]
    public async Task Crud_GeneratesIdsRejectsDuplicatesAndRedactsOnlyResponses()
    {
        await SeedAsync();
        await using var server = Server();
        var input = Config();
        input.Id = null;
        var created = await server.CreateTaskPushNotificationConfigAsync(input);
        Assert.True(Guid.TryParse(created.Id, out _));
        Assert.Null(input.Id);
        Assert.Equal("credential", created.Authentication!.Credentials);
        Assert.Equal("token", created.Token);
        created.Authentication.Credentials = "changed-by-client";
        var get = await server.GetTaskPushNotificationConfigAsync(new() { TaskId = "task", Id = created.Id! });
        Assert.Null(get.Token);
        Assert.Null(get.Authentication!.Credentials);
        Assert.Equal("Bearer", get.Authentication.Scheme);
        var list = await server.ListTaskPushNotificationConfigsAsync(new() { TaskId = "task" });
        Assert.Null(Assert.Single(list.Configs!).Token);
        Assert.Null(list.Configs![0].Authentication!.Credentials);
        Assert.Equal(string.Empty, list.NextPageToken);
        Assert.Equal("credential", (await _configs.GetAsync("task", created.Id!))!.Configuration.Authentication!.Credentials);
        await ErrorAsync(A2AErrorCode.InvalidParams, () => server.CreateTaskPushNotificationConfigAsync(created));
        await server.DeleteTaskPushNotificationConfigAsync(new() { TaskId = "task", Id = created.Id! });
        await server.DeleteTaskPushNotificationConfigAsync(new() { TaskId = "task", Id = created.Id! });
        await ErrorAsync(A2AErrorCode.TaskNotFound, () => server.GetTaskPushNotificationConfigAsync(new() { TaskId = "task", Id = created.Id! }));
    }

    [Fact]
    public async Task AllCrud_RequireAnExistingTaskAndValidIdentifiers()
    {
        await using var server = Server();
        await ErrorAsync(A2AErrorCode.TaskNotFound, () => server.CreateTaskPushNotificationConfigAsync(Config()));
        await ErrorAsync(A2AErrorCode.TaskNotFound, () => server.GetTaskPushNotificationConfigAsync(new() { TaskId = "task", Id = "id" }));
        await ErrorAsync(A2AErrorCode.TaskNotFound, () => server.ListTaskPushNotificationConfigsAsync(new() { TaskId = "task" }));
        await ErrorAsync(A2AErrorCode.TaskNotFound, () => server.DeleteTaskPushNotificationConfigAsync(new() { TaskId = "task", Id = "id" }));
        await SeedAsync();
        await ErrorAsync(A2AErrorCode.InvalidParams, () => server.GetTaskPushNotificationConfigAsync(new() { TaskId = "task", Id = "" }));
        await ErrorAsync(A2AErrorCode.InvalidParams, () => server.DeleteTaskPushNotificationConfigAsync(new() { TaskId = "task", Id = null! }));
        await ErrorAsync(A2AErrorCode.InvalidParams, () => server.ListTaskPushNotificationConfigsAsync(new() { TaskId = "" }));
        await ErrorAsync(A2AErrorCode.InvalidParams, () => server.ListTaskPushNotificationConfigsAsync(new() { TaskId = "task", PageSize = -1 }));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server.CreateTaskPushNotificationConfigAsync(Config(), canceled.Token));
    }

    [Theory]
    [InlineData("segment/child")]
    [InlineData("segment\\child")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("%")]
    [InlineData("%2f")]
    [InlineData("segment%252Fchild")]
    [InlineData("%2e%2e")]
    public async Task UnsafeConfigId_IsInvalidForCrudAndBothInlineModes(string id)
    {
        await SeedAsync();
        await using var server = Server();
        var config = Config();
        config.Id = id;
        await ErrorAsync(A2AErrorCode.InvalidParams, () => server.CreateTaskPushNotificationConfigAsync(config));
        await ErrorAsync(A2AErrorCode.InvalidParams, () => server.GetTaskPushNotificationConfigAsync(new() { TaskId = "task", Id = id }));
        await ErrorAsync(A2AErrorCode.InvalidParams, () => server.DeleteTaskPushNotificationConfigAsync(new() { TaskId = "task", Id = id }));
        var request = Request(inline: true);
        request.Configuration!.TaskPushNotificationConfig!.Id = id;
        await ErrorAsync(A2AErrorCode.InvalidParams, () => server.SendMessageAsync(request));
        await ErrorAsync(A2AErrorCode.InvalidParams, async () =>
        {
            await foreach (var response in server.SendStreamingMessageAsync(request))
            {
                Assert.NotNull(response);
            }
        });
        Assert.Equal(0, _handler.Calls);
        Assert.Empty(await _configs.GetAllAsync("task"));
        Assert.Empty(_sender.Events);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("credentials")]
    [InlineData("scheme")]
    public async Task IllegalHeaders_AreRejectedBeforeHandlerOrStorage(string field)
    {
        await using var server = Server();
        var request = Request(inline: true);
        var config = request.Configuration!.TaskPushNotificationConfig!;
        switch (field)
        {
            case "token": config.Token = "bad\r\nheader"; break;
            case "credentials": config.Authentication!.Credentials = "bad\nheader"; break;
            case "scheme": config.Authentication!.Scheme = "bad scheme"; break;
        }
        await ErrorAsync(A2AErrorCode.InvalidParams, () => server.SendMessageAsync(request));
        Assert.Equal(0, _handler.Calls);
        Assert.Empty(_sender.Events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MissingUrlOrAuthenticationScheme_IsInvalidBeforeCloning(string? value)
    {
        await using var server = Server();
        var request = Request(inline: true);
        request.Configuration!.TaskPushNotificationConfig!.Url = value!;
        await ErrorAsync(A2AErrorCode.InvalidParams, () => server.SendMessageAsync(request));
        request = Request(inline: true);
        request.Configuration!.TaskPushNotificationConfig!.Authentication!.Scheme = value!;
        await ErrorAsync(A2AErrorCode.InvalidParams, () => server.SendMessageAsync(request));
        Assert.Equal(0, _handler.Calls);
    }

    [Fact]
    public async Task UrlPolicy_IsAppliedBeforeHandlerAndBeforeCreate()
    {
        _validator.Reject = true;
        await using var server = Server();
        await ErrorAsync(A2AErrorCode.InvalidParams, () => server.SendMessageAsync(Request(inline: true)));
        await SeedAsync();
        await ErrorAsync(A2AErrorCode.InvalidParams, () => server.CreateTaskPushNotificationConfigAsync(Config()));
        Assert.Equal(0, _handler.Calls);
        Assert.Empty(await _configs.GetAllAsync("task"));
    }

    [Fact]
    public async Task InlineTask_UsesResolvedIdentityAndEnqueuesOnlyAfterPersistenceOutsideLock()
    {
        _handler.Execute = EmitLifecycleAsync;
        var verified = new List<bool>();
        _sender.OnEnqueue = async (registration, response, cancellationToken) =>
        {
            var config = registration.Configuration;
            var task = await _tasks.GetTaskAsync(config.TaskId!, cancellationToken);
            var stored = await _configs.GetAsync(config.TaskId!, config.Id!, cancellationToken);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var lease = await _notifier.AcquireTaskLockAsync(config.TaskId!, timeout.Token);
            verified.Add(task is not null && stored is not null && response.PayloadCase != StreamResponseCase.None);
        };
        await using var server = Server();
        var request = Request(inline: true);
        request.Configuration!.TaskPushNotificationConfig!.TaskId = "not-authoritative";
        var result = await server.SendMessageAsync(request);
        Assert.NotNull(result.Task);
        Assert.Equal(TaskState.Completed, result.Task.Status.State);
        Assert.Equal(_handler.TaskId, result.Task.Id);
        Assert.Equal(5, _sender.Events.Count);
        Assert.Equal(5, verified.Count);
        Assert.All(verified, check => Assert.True(check));
        Assert.All(_sender.Events, item => Assert.Equal(result.Task.Id, item.Config.Configuration.TaskId));
        Assert.Empty(await _configs.GetAllAsync("not-authoritative"));
        Assert.Equal(
            new[] { StreamResponseCase.Task, StreamResponseCase.StatusUpdate, StreamResponseCase.ArtifactUpdate, StreamResponseCase.Message, StreamResponseCase.StatusUpdate },
            _sender.Events.Select(item => item.Response.PayloadCase));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task InlineMessage_PreservesDirectMessageAcrossRequestModes(bool streaming, bool immediate)
    {
        _handler.Execute = EmitMessageAsync;
        await using var server = Server();
        var request = Request(inline: true);
        request.Configuration!.ReturnImmediately = immediate;
        AgentTask? task;
        Message? message;
        if (streaming)
        {
            var events = new List<StreamResponse>();
            await foreach (var response in server.SendStreamingMessageAsync(request))
            {
                events.Add(response);
            }
            var result = Assert.Single(events);
            task = result.Task;
            message = result.Message;
        }
        else
        {
            var result = await server.SendMessageAsync(request);
            task = result.Task;
            message = result.Message;
        }
        Assert.Null(task);
        Assert.NotNull(message);
        Assert.Null(await _tasks.GetTaskAsync(_handler.TaskId!));
        Assert.Empty(await _configs.GetAllAsync(_handler.TaskId!));
        Assert.Empty(_sender.Events);
        Assert.Contains(_logger.Messages, entry => entry.Contains("inline_unused", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MessageThenHandlerFailure_DoesNotCreateTaskOrConfig(bool streaming)
    {
        _handler.Execute = async (context, queue, cancellationToken) =>
        {
            await EmitMessageAsync(context, queue, cancellationToken);
            throw new InvalidOperationException("handler failed");
        };
        await using var server = Server();
        var request = Request(inline: true);
        if (streaming)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var response in server.SendStreamingMessageAsync(request))
                {
                    Assert.Null(response.Task);
                    Assert.NotNull(response.Message);
                }
            });
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => server.SendMessageAsync(request));
        }
        Assert.Null(await _tasks.GetTaskAsync(_handler.TaskId!));
        Assert.Empty(await _configs.GetAllAsync(_handler.TaskId!));
        Assert.Empty(_sender.Events);
    }

    [Fact]
    public async Task NoInlineConfig_PreservesMessageOnlyBehavior()
    {
        _handler.Execute = EmitMessageAsync;
        await using var server = Server();
        var result = await server.SendMessageAsync(Request(inline: false));
        Assert.NotNull(result.Message);
        Assert.Null(result.Task);
        Assert.Empty(_sender.Events);
        Assert.Equal(0, _validator.Calls);
    }

    [Fact]
    public async Task MultipleConfigs_ReceiveEveryPersistedUpdate()
    {
        await SeedAsync();
        await using var server = Server();
        await server.CreateTaskPushNotificationConfigAsync(Config());
        var second = Config();
        second.Id = "second";
        await server.CreateTaskPushNotificationConfigAsync(second);
        _handler.Execute = async (context, queue, cancellationToken) =>
        {
            var updater = new TaskUpdater(queue, context.TaskId, context.ContextId);
            await updater.StartWorkAsync(cancellationToken: cancellationToken);
            await updater.CompleteAsync(cancellationToken: cancellationToken);
        };
        var request = Request(inline: false);
        request.Message.TaskId = "task";
        var result = await server.SendMessageAsync(request);
        Assert.Equal(TaskState.Completed, result.Task!.Status.State);
        Assert.Equal(TaskState.Completed, (await _tasks.GetTaskAsync("task"))!.Status.State);
        Assert.Equal(6, _sender.Events.Count); // incoming history message plus two statuses, for two configs
    }

    [Fact]
    public async Task ConcurrentCancel_KeepsEnqueueOrderWithoutHoldingTaskLock()
    {
        await SeedAsync();
        await using var server = Server();
        await server.CreateTaskPushNotificationConfigAsync(Config());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = _notifier.CreateChannel("task");
        _sender.OnEnqueue = async (_, _, cancellationToken) =>
        {
            if (_sender.Events.Count == 1)
            {
                entered.TrySetResult();
                await resume.Task.WaitAsync(cancellationToken);
            }
        };
        _handler.Execute = (context, queue, cancellationToken) =>
            new TaskUpdater(queue, context.TaskId, context.ContextId).CompleteAsync(cancellationToken: cancellationToken).AsTask();
        var request = Request(inline: false);
        request.Message.TaskId = "task";
        var sending = server.SendMessageAsync(request);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var canceling = server.CancelTaskAsync(new() { Id = "task" });
            Assert.NotNull((await channel.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Message);
            var canceledEvent = await channel.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(TaskState.Canceled, canceledEvent.StatusUpdate!.Status.State);
            Assert.Equal(TaskState.Canceled, (await _tasks.GetTaskAsync("task"))!.Status.State);
            Assert.False(canceling.IsCompleted);
            resume.TrySetResult();
            await canceling;
            Assert.Equal(TaskState.Canceled, (await sending).Task!.Status.State);
            Assert.Equal(2, _sender.Events.Count);
            Assert.NotNull(_sender.Events[0].Response.Message);
            Assert.Equal(TaskState.Canceled, _sender.Events[1].Response.StatusUpdate!.Status.State);
        }
        finally
        {
            resume.TrySetResult();
            _notifier.RemoveChannel("task", channel);
        }
    }

    [Fact]
    public async Task StoreFailures_PropagateAndEnumerationFailurePreventsTaskCommit()
    {
        await SeedAsync();
        var store = new FailingStore(_configs) { FailSave = true };
        await using var server = Server(store: store);
        await Assert.ThrowsAsync<IOException>(() => server.CreateTaskPushNotificationConfigAsync(Config()));
        Assert.Empty(await _configs.GetAllAsync("task"));
        store.FailSave = false;
        await server.CreateTaskPushNotificationConfigAsync(Config());
        store.FailEnumeration = true;
        _handler.Execute = (context, queue, cancellationToken) =>
            new TaskUpdater(queue, context.TaskId, context.ContextId).CompleteAsync(cancellationToken: cancellationToken).AsTask();
        var request = Request(inline: false);
        request.Message.TaskId = "task";
        await Assert.ThrowsAsync<IOException>(() => server.SendMessageAsync(request));
        Assert.Empty(_sender.Events);
        Assert.Equal(TaskState.Submitted, (await _tasks.GetTaskAsync("task"))!.Status.State);
        Assert.Equal(0, _handler.Calls);
    }

    [Fact]
    public async Task AdmissionFailure_IsNotReportedAsSuccessfulExecution()
    {
        await SeedAsync();
        await using var server = Server();
        await server.CreateTaskPushNotificationConfigAsync(Config());
        _sender.Fail = true;
        var request = Request(inline: false);
        request.Message.TaskId = "task";
        await Assert.ThrowsAsync<PushNotificationAdmissionException>(() => server.SendMessageAsync(request));
        Assert.Equal(TaskState.Submitted, (await _tasks.GetTaskAsync("task"))!.Status.State);
        Assert.NotNull(await _configs.GetAsync("task", "config"));
        Assert.Equal(0, _handler.Calls);
    }

    [Fact]
    public async Task RegistrationAfterTaskCompletion_ReceivesTheCurrentSnapshotAndRemainsReadable()
    {
        await _tasks.SaveTaskAsync("task", new AgentTask
        {
            Id = "task",
            ContextId = "context",
            Status = new TaskStatus { State = TaskState.Completed },
        });
        await using var server = Server();
        await server.CreateTaskPushNotificationConfigAsync(Config());
        var sent = Assert.Single(_sender.Events);
        Assert.Equal(TaskState.Completed, sent.Response.Task!.Status.State);
        Assert.NotNull(await _configs.GetAsync("task", "config"));
    }

    [Fact]
    public async Task TaskSaveFailure_DoesNotActivateInlineConfigOrPublish()
    {
        var taskStore = new TaskStoreProbe(_tasks) { FailSave = true };
        _handler.Execute = EmitLifecycleAsync;
        await using var server = Server(tasks: taskStore);
        await Assert.ThrowsAsync<IOException>(() => server.SendMessageAsync(Request(inline: true)));
        Assert.Null(await _tasks.GetTaskAsync(_handler.TaskId!));
        Assert.Empty(await _configs.GetAllAsync(_handler.TaskId!));
        Assert.Empty(_sender.Events);
    }

    [Fact]
    public async Task InlineConfigActivationFailure_IsAnErrorAfterTaskPersistence_NotAtomicSuccess()
    {
        var configStore = new FailingStore(_configs) { FailSave = true };
        _handler.Execute = EmitLifecycleAsync;
        await using var server = Server(store: configStore);
        await Assert.ThrowsAsync<IOException>(() => server.SendMessageAsync(Request(inline: true)));
        var persisted = await _tasks.GetTaskAsync(_handler.TaskId!);
        Assert.NotNull(persisted);
        Assert.Equal(TaskState.Submitted, persisted.Status.State);
        Assert.Empty(await _configs.GetAllAsync(_handler.TaskId!));
        Assert.Empty(_sender.Events);
    }

    [Fact]
    public async Task RegistrationRacingTerminalCommitAndLastSubscriberRemoval_GetsOneSnapshot()
    {
        await SeedAsync();
        var taskStore = new TaskStoreProbe(_tasks) { GateTerminal = true };
        _handler.Execute = (context, queue, cancellationToken) =>
            new TaskUpdater(queue, context.TaskId, context.ContextId).CompleteAsync(cancellationToken: cancellationToken).AsTask();
        await using var server = Server(tasks: taskStore);
        var subscriber = _notifier.CreateChannel("task");
        var request = Request(inline: false);
        request.Message.TaskId = "task";
        var sending = server.SendMessageAsync(request);
        Task<TaskPushNotificationConfig>? creating = null;
        try
        {
            await taskStore.TerminalSaved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            _notifier.RemoveChannel("task", subscriber);
            creating = server.CreateTaskPushNotificationConfigAsync(Config());
            Assert.False(creating.IsCompleted, "Registration must share the still-held task lock after subscriber removal.");
        }
        finally
        {
            taskStore.Release.TrySetResult();
            _notifier.RemoveChannel("task", subscriber);
        }
        Assert.Equal(TaskState.Completed, (await sending).Task!.Status.State);
        Assert.NotNull(await creating!);
        var publication = Assert.Single(_sender.Events);
        Assert.Equal(TaskState.Completed, publication.Response.Task!.Status.State);
        Assert.Equal("config", publication.Config.Configuration.Id);
        Assert.NotNull(await _configs.GetAsync("task", "config"));
    }

    private A2AServer Server(bool? enabled = true, IPushNotificationStore? store = null, ITaskStore? tasks = null)
    {
        return new A2AServer(_handler, tasks ?? _tasks, _notifier, _logger,
            new A2AServerOptions(),
            new AgentCard { Capabilities = new AgentCapabilities { PushNotifications = enabled } },
            store ?? _configs, _validator, _sender);
    }

    private Task SeedAsync()
    {
        return _tasks.SaveTaskAsync("task", new AgentTask
        {
            Id = "task",
            ContextId = "context",
            Status = new TaskStatus { State = TaskState.Submitted },
        });
    }

    private TaskPushNotificationConfig Config() => new()
    {
        TaskId = "task",
        Id = "config",
        Url = "https://callback.example/notify",
        Token = "token",
        Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = "credential" },
        Tenant = _tenant,
    };

    private SendMessageRequest Request(bool inline) => new()
    {
        Message = new Message { MessageId = "user", Role = Role.User, Parts = [Part.FromText("request")] },
        Configuration = new SendMessageConfiguration { TaskPushNotificationConfig = inline ? Config() : null },
    };

    private async Task EmitLifecycleAsync(RequestContext context, AgentEventQueue queue, CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(queue, context.TaskId, context.ContextId);
        await updater.SubmitAsync(cancellationToken: cancellationToken);
        await updater.StartWorkAsync(cancellationToken: cancellationToken);
        await updater.AddArtifactAsync([Part.FromText("result")], artifactId: "artifact", cancellationToken: cancellationToken);
        await EmitMessageAsync(context, queue, cancellationToken);
        await updater.CompleteAsync(cancellationToken: cancellationToken);
    }

    private Task EmitMessageAsync(RequestContext context, AgentEventQueue queue, CancellationToken cancellationToken)
    {
        return queue.EnqueueMessageAsync(new Message
        {
            MessageId = "reply",
            ContextId = context.ContextId,
            TaskId = _handler.TaskId,
            Role = Role.Agent,
            Parts = [Part.FromText("response")],
        }, cancellationToken).AsTask();
    }

    private async Task ErrorAsync(A2AErrorCode expected, Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<A2AException>(action);
        Assert.Equal(expected, exception.ErrorCode);
        Assert.Empty(_sender.Events);
    }

    private sealed class Handler : IAgentHandler
    {
        internal Func<RequestContext, AgentEventQueue, CancellationToken, Task> Execute { get; set; } = (_, _, _) => Task.CompletedTask;
        internal int Calls { get; private set; }
        internal string? TaskId { get; private set; }
        public Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            Calls++;
            TaskId = context.TaskId;
            return Execute(context, eventQueue, cancellationToken);
        }
        public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) =>
            new TaskUpdater(eventQueue, context.TaskId, context.ContextId).CancelAsync(cancellationToken: cancellationToken).AsTask();
    }

    private sealed class RecordingValidator : IPushNotificationUrlValidator
    {
        internal int Calls { get; private set; }
        internal bool Reject { get; set; }
        public Task<IReadOnlyList<IPAddress>> ValidateAsync(string url, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (Reject)
            {
                throw new A2AException("Destination rejected.", A2AErrorCode.InvalidParams);
            }
            return Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Loopback]);
        }
    }

    private sealed class RecordingSender : IPushNotificationSender
    {
        internal List<(PushNotificationConfigSnapshot Config, StreamResponse Response)> Events { get; } = [];
        internal Func<PushNotificationConfigSnapshot, StreamResponse, CancellationToken, Task>? OnEnqueue { get; set; }
        internal bool Fail { get; set; }
        public async Task EnqueueAsync(PushNotificationConfigSnapshot registration, StreamResponse response, CancellationToken cancellationToken = default)
        {
            var copy = JsonSerializer.Deserialize<StreamResponse>(
                JsonSerializer.Serialize(response, A2AJsonUtilities.DefaultOptions), A2AJsonUtilities.DefaultOptions)!;
            Events.Add((registration, copy));
            if (OnEnqueue is not null)
            {
                await OnEnqueue(registration, response, cancellationToken);
            }
            if (Fail)
            {
                throw new IOException("delivery infrastructure unavailable");
            }
        }
    }

    private sealed class FailingStore(IPushNotificationStore inner) : IPushNotificationStore
    {
        internal bool FailSave { get; set; }
        internal bool FailEnumeration { get; set; }
        public Task<PushNotificationConfigSnapshot> SaveAsync(TaskPushNotificationConfig config, CancellationToken cancellationToken = default) =>
            FailSave ? throw new IOException("store unavailable") : inner.SaveAsync(config, cancellationToken);
        public Task<PushNotificationConfigSnapshot?> GetAsync(string taskId, string configId, CancellationToken cancellationToken = default) =>
            inner.GetAsync(taskId, configId, cancellationToken);
        public Task<IReadOnlyList<PushNotificationConfigSnapshot>> GetAllAsync(string taskId, CancellationToken cancellationToken = default) =>
            FailEnumeration ? throw new IOException("store unavailable") : inner.GetAllAsync(taskId, cancellationToken);
        public Task<ListTaskPushNotificationConfigsResponse> ListAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken cancellationToken = default) =>
            inner.ListAsync(request, cancellationToken);
        public Task DeleteAsync(string taskId, string configId, string? version = null, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(taskId, configId, version, cancellationToken);
    }

    private sealed class TaskStoreProbe(ITaskStore inner) : ITaskStore
    {
        internal bool FailSave { get; set; }
        internal bool GateTerminal { get; set; }
        internal TaskCompletionSource TerminalSaved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
            inner.GetTaskAsync(taskId, cancellationToken);

        public async Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default)
        {
            if (FailSave)
            {
                throw new IOException("task store unavailable");
            }
            await inner.SaveTaskAsync(taskId, task, cancellationToken);
            if (GateTerminal &&
                task.Status.State == TaskState.Completed)
            {
                TerminalSaved.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
        }

        public Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
            inner.DeleteTaskAsync(taskId, cancellationToken);
        public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default) =>
            inner.ListTasksAsync(request, cancellationToken);
    }

    private sealed class CaptureLogger : ILogger<A2AServer>
    {
        internal System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Enqueue(formatter(state, exception));
    }
}

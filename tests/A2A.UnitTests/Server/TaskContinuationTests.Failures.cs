namespace A2A.UnitTests.Server;

public sealed partial class TaskContinuationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FaultedQueueCompletion_ReportsProducerFailureAndCommittedProgress(bool committed, bool continuationError)
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        await using var server = Server();
        Exception failure = continuationError
            ? await Assert.ThrowsAsync<TaskContinuationException>(() =>
                server.ContinueTaskAsync("missing", (_, _) => Task.CompletedTask))
            : new InvalidOperationException("queue failed");

        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", async (context, cancellationToken) =>
            {
                if (committed)
                {
                    await context.EventQueue.EnqueueMessageAsync(Message("committed"), cancellationToken);
                }
                context.EventQueue.Complete(failure);
            }));

        Assert.Equal(TaskContinuationFailureKind.Producer, error.FailureKind);
        Assert.Same(failure, error.InnerException);
        Assert.Equal(committed ? 1 : 0, error.Progress!.PersistedEventCount);
        Assert.Equal(committed ? 1 : 0, error.Progress.AcceptedPushNotificationCount);
        Assert.Equal(committed ? ["original", "committed"] : ["original"],
            error.Progress.Task.History!.Select(message => message.MessageId));
        Assert.Equal(committed ? ["committed"] : [], _sender.Events);
        Assert.Equal(committed ? 2 : 1, (await _tasks.GetTaskAsync("task"))!.History!.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicationFailure_BeforeFaultedQueueCompletion_PreservesItsOwnStage(bool admission)
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var store = new TaskStoreProbe(_tasks) { FailSave = !admission };
        var admissionError = new IOException("admission failed");
        _sender.OnEnqueue = (_, _, _) => Task.FromException(admissionError);
        await using var server = Server(tasks: store);

        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", async (context, cancellationToken) =>
            {
                await context.EventQueue.EnqueueMessageAsync(Message("committed"), cancellationToken);
                context.EventQueue.Complete(new InvalidOperationException("queue failed"));
            }));

        Assert.Equal(admission ? TaskContinuationFailureKind.PushAdmission : TaskContinuationFailureKind.TaskStore,
            error.FailureKind);
        if (admission)
        {
            Assert.Same(admissionError, error.InnerException!.InnerException);
        }
        else
        {
            Assert.IsType<IOException>(error.InnerException);
        }
        Assert.Equal(admission ? 1 : 0, error.Progress!.PersistedEventCount);
        Assert.Equal(0, error.Progress.AcceptedPushNotificationCount);
        Assert.Empty(_sender.Events);
        Assert.Equal(admission ? 2 : 1, (await _tasks.GetTaskAsync("task"))!.History!.Count);
    }

    [Fact]
    public async Task ProducerPropagatingAnotherContinuationFailure_PreservesProducerStageAndInnerError()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        await using var server = Server();
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", async (context, cancellationToken) =>
            {
                await context.EventQueue.EnqueueMessageAsync(Message("committed"), cancellationToken);
                await server.ContinueTaskAsync("missing", (_, _) => Task.CompletedTask, cancellationToken);
            }));
        Assert.Equal(TaskContinuationFailureKind.Producer, error.FailureKind);
        var inner = Assert.IsType<TaskContinuationException>(error.InnerException);
        Assert.Equal(TaskContinuationFailureKind.TaskNotFound, inner.FailureKind);
        Assert.Null(inner.Progress);
        Assert.Equal("task", error.Progress!.Task.Id);
        Assert.Equal(1, error.Progress.PersistedEventCount);
        Assert.Equal(1, error.Progress.AcceptedPushNotificationCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TaskStoreFailure_IsDistinctAndDoesNotPublishUnpersistedEvents(bool reading)
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var store = new TaskStoreProbe(_tasks) { FailRead = reading, FailSave = !reading };
        await using var server = Server(tasks: store);
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", (context, cancellationToken) =>
                context.EventQueue.EnqueueMessageAsync(Message("not-committed"), cancellationToken).AsTask()));
        Assert.Equal(TaskContinuationFailureKind.TaskStore, error.FailureKind);
        Assert.IsType<IOException>(error.InnerException);
        Assert.Empty(_sender.Events);
        Assert.Single((await _tasks.GetTaskAsync("task"))!.History!);
        Assert.Equal(0, error.Progress?.PersistedEventCount ?? 0);
    }

    [Fact]
    public async Task PushStoreFailure_IsDistinctBeforeProducer()
    {
        await SeedAsync();
        var store = new RegistrationStoreProbe(_registrations) { FailRead = true };
        await using var server = Server(registrations: store);
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", (_, _) => throw new InvalidOperationException("must not run")));
        Assert.Equal(TaskContinuationFailureKind.PushNotificationStore, error.FailureKind);
        Assert.IsType<IOException>(error.InnerException);
        Assert.Equal(0, error.Progress!.PersistedEventCount);
    }

    [Fact]
    public async Task PushStoreGenerationReadFailure_PreservesPartialFanoutAndStoreStage()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config("first"));
        await _registrations.SaveAsync(Config("second"));
        var store = new RegistrationStoreProbe(_registrations) { FailGenerationReadId = "second" };
        await using var server = Server(registrations: store);
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", (context, cancellationToken) =>
                context.EventQueue.EnqueueMessageAsync(Message("committed"), cancellationToken).AsTask()));
        Assert.Equal(TaskContinuationFailureKind.PushNotificationStore, error.FailureKind);
        Assert.IsType<IOException>(error.InnerException);
        Assert.Equal(1, error.Progress!.PersistedEventCount);
        Assert.Equal(1, error.Progress.AcceptedPushNotificationCount);
        Assert.Equal(["committed"], _sender.Events);
        Assert.Equal(2, (await _tasks.GetTaskAsync("task"))!.History!.Count);
    }

    [Fact]
    public async Task ProjectionFailure_DoesNotPersistOrPublishTheInvalidEvent()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        await using var server = Server();
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", (context, cancellationToken) =>
                context.EventQueue.EnqueueArtifactUpdateAsync(new()
                {
                    TaskId = "task", ContextId = "context", Artifact = null!,
                }, cancellationToken).AsTask()));
        Assert.Equal(TaskContinuationFailureKind.Projection, error.FailureKind);
        Assert.Equal(0, error.Progress!.PersistedEventCount);
        Assert.Empty(_sender.Events);
        Assert.Single((await _tasks.GetTaskAsync("task"))!.Artifacts!);
    }

    [Fact]
    public async Task MutatingContextSnapshot_DoesNotBypassPersistenceWithANonCloningStore()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var stored = (await _tasks.GetTaskAsync("task"))!;
        var store = new TaskStoreProbe(_tasks) { ReadSnapshot = stored };
        await using var server = Server(tasks: store);
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", (context, _) =>
            {
                context.Task.History!.Clear();
                context.Task.Artifacts!.Clear();
                context.Task.Status.State = TaskState.Completed;
                context.Task.Id = "changed";
                Assert.Equal("task", context.TaskId);
                throw new InvalidOperationException("producer failed");
            }));
        Assert.Equal(TaskContinuationFailureKind.Producer, error.FailureKind);
        Assert.Equal("task", stored.Id);
        Assert.Equal(TaskState.Working, stored.Status.State);
        Assert.Single(stored.History!);
        Assert.Single(stored.Artifacts!);
        Assert.Equal(TaskState.Working, error.Progress!.Task.Status.State);
        Assert.Single(error.Progress.Task.History!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyOrAmbiguousPayload_IsInvalidBeforePersistence(bool ambiguous)
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        await using var server = Server();
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", (context, cancellationToken) =>
                context.EventQueue.WriteAsync(ambiguous
                    ? new StreamResponse { Message = Message("bad"), Task = context.Task }
                    : new StreamResponse(), cancellationToken).AsTask()));
        Assert.Equal(TaskContinuationFailureKind.InvalidEvent, error.FailureKind);
        Assert.Equal(0, error.Progress!.PersistedEventCount);
        Assert.Empty(_sender.Events);
    }

    [Fact]
    public async Task CancellationSwallowedByProducer_StillReportsCancellation()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        await using var server = Server();
        using var cancellation = new CancellationTokenSource();
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", async (_, _) => await cancellation.CancelAsync(), cancellation.Token));
        Assert.Equal(TaskContinuationFailureKind.Cancellation, error.FailureKind);
        Assert.Equal(0, error.Progress!.PersistedEventCount);
    }

    [Fact]
    public async Task DeletionBetweenPersistenceAndAdmission_IsNotAccepted()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var store = new TaskStoreProbe(_tasks)
        {
            AfterSave = () => _registrations.DeleteAsync("task", "config"),
        };
        await using var server = Server(tasks: store);
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", (context, cancellationToken) =>
                context.EventQueue.EnqueueMessageAsync(Message("committed"), cancellationToken).AsTask()));
        Assert.Equal(TaskContinuationFailureKind.PushAdmission, error.FailureKind);
        Assert.Equal(1, error.Progress!.PersistedEventCount);
        Assert.Equal(0, error.Progress.AcceptedPushNotificationCount);
        Assert.Empty(_sender.Events);
        Assert.Equal(2, (await _tasks.GetTaskAsync("task"))!.History!.Count);
    }

    [Fact]
    public async Task DeletedGeneration_DoesNotBorrowReplacementRegistration()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var store = new TaskStoreProbe(_tasks)
        {
            AfterSave = async () =>
            {
                await _registrations.DeleteAsync("task", "config");
                await _registrations.SaveAsync(Config());
            },
        };
        await using var server = Server(tasks: store);
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() =>
            server.ContinueTaskAsync("task", (context, cancellationToken) =>
                context.EventQueue.EnqueueMessageAsync(Message("committed"), cancellationToken).AsTask()));
        Assert.Equal(TaskContinuationFailureKind.PushAdmission, error.FailureKind);
        Assert.Equal(0, error.Progress!.AcceptedPushNotificationCount);
        Assert.NotNull(await _registrations.GetAsync("task", "config"));
        Assert.Empty(_sender.Events);
    }

    private sealed class TaskStoreProbe(ITaskStore inner) : ITaskStore
    {
        public bool FailRead { get; init; }
        public bool FailSave { get; init; }
        public AgentTask? ReadSnapshot { get; init; }
        public Func<Task>? AfterSave { get; init; }

        public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
            FailRead ? Task.FromException<AgentTask?>(new IOException("task read"))
                : ReadSnapshot is not null ? Task.FromResult<AgentTask?>(ReadSnapshot)
                : inner.GetTaskAsync(taskId, cancellationToken);
        public async Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default)
        {
            if (FailSave)
            {
                throw new IOException("task save");
            }
            await inner.SaveTaskAsync(taskId, task, cancellationToken);
            if (AfterSave is not null)
            {
                await AfterSave();
            }
        }
        public Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
            inner.DeleteTaskAsync(taskId, cancellationToken);
        public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default) =>
            inner.ListTasksAsync(request, cancellationToken);
    }

    private sealed class RegistrationStoreProbe(IPushNotificationStore inner) : IPushNotificationStore
    {
        public bool FailRead { get; init; }
        public string? FailGenerationReadId { get; init; }
        public Task<PushNotificationConfigSnapshot> SaveAsync(TaskPushNotificationConfig config, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(config, cancellationToken);
        public Task<PushNotificationConfigSnapshot?> GetAsync(string taskId, string configId, CancellationToken cancellationToken = default) =>
            configId == FailGenerationReadId
                ? Task.FromException<PushNotificationConfigSnapshot?>(new IOException("registration generation read"))
                : inner.GetAsync(taskId, configId, cancellationToken);
        public Task<IReadOnlyList<PushNotificationConfigSnapshot>> GetAllAsync(string taskId, CancellationToken cancellationToken = default) =>
            FailRead ? Task.FromException<IReadOnlyList<PushNotificationConfigSnapshot>>(new IOException("registration read"))
                : inner.GetAllAsync(taskId, cancellationToken);
        public Task<ListTaskPushNotificationConfigsResponse> ListAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken cancellationToken = default) =>
            inner.ListAsync(request, cancellationToken);
        public Task DeleteAsync(string taskId, string configId, string? version = null, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(taskId, configId, version, cancellationToken);
    }
}

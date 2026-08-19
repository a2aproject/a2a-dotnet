using Microsoft.Extensions.Logging.Abstractions;

namespace A2A.UnitTests.GitHubIssues
{
    /// <summary>
    /// Regression tests for issue #401: a forced terminal transition must not
    /// overwrite a terminal state that another writer has already persisted.
    /// The policy is "first persisted terminal state wins", enforced atomically
    /// under the per-task lock in <c>A2AServer.ApplyEventAsync</c>.
    ///
    /// Both writers that can force a terminal state (cancellation and background
    /// failure) check <c>IsTerminal</c> before taking the lock, so each test drives
    /// the race window between that optimistic check and the locked apply, then
    /// asserts the earlier terminal state survives.
    /// </summary>
    public sealed class Issue401
    {
        [Fact]
        public async Task CancelDoesNotOverwriteConcurrentlyPersistedTerminalState()
        {
            var notifier = new ChannelEventNotifier();
            var store = new InMemoryTaskStore();
            var handler = new HookAgentHandler();
            await using var server = new A2AServer(handler, store, notifier, NullLogger<A2AServer>.Instance);

            const string taskId = "issue-401-cancel";
            await store.SaveTaskAsync(taskId, TaskInState(taskId, TaskState.Working));

            var cancelReachedHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCancel = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // The cancel handler pauses after CancelTaskAsync has passed its pre-lock
            // terminal check but before it emits the Canceled event.
            handler.OnCancel = async (ctx, eq, ct) =>
            {
                cancelReachedHandler.TrySetResult();
                await releaseCancel.Task.ConfigureAwait(false);
                await new TaskUpdater(eq, ctx.TaskId, ctx.ContextId).CancelAsync(cancellationToken: ct).ConfigureAwait(false);
                eq.Complete();
            };

            var cancelTask = server.CancelTaskAsync(new CancelTaskRequest { Id = taskId });
            await cancelReachedHandler.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // A concurrent writer persists a terminal (Completed) state inside the race window.
            await store.SaveTaskAsync(taskId, TaskInState(taskId, TaskState.Completed));

            releaseCancel.TrySetResult();
            await cancelTask.WaitAsync(TimeSpan.FromSeconds(5));

            var persisted = await store.GetTaskAsync(taskId);
            Assert.NotNull(persisted);
            Assert.Equal(TaskState.Completed, persisted!.Status.State);
        }

        [Fact]
        public async Task BackgroundFailureDoesNotOverwriteConcurrentlyPersistedTerminalState()
        {
            var notifier = new ChannelEventNotifier();
            var store = new InjectingTaskStore();
            var handler = new HookAgentHandler();
            await using var server = new A2AServer(handler, store, notifier, NullLogger<A2AServer>.Instance);

            var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseThrow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            handler.OnExecute = async (ctx, eq, ct) =>
            {
                var updater = new TaskUpdater(eq, ctx.TaskId, ctx.ContextId);
                await updater.SubmitAsync(cancellationToken: ct).ConfigureAwait(false);
                await updater.StartWorkAsync(cancellationToken: ct).ConfigureAwait(false);
                handlerStarted.TrySetResult();
                await releaseThrow.Task.ConfigureAwait(false);
                throw new InvalidOperationException("Simulated background failure");
            };

            var result = await server.SendMessageAsync(new SendMessageRequest
            {
                Message = new Message { MessageId = "u1", Parts = [Part.FromText("hi")], Role = Role.User },
                Configuration = new SendMessageConfiguration { ReturnImmediately = true },
            });
            var taskId = result.Task!.Id;

            await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForStateAsync(store, taskId, TaskState.Working);

            // Arm the store so the first read taken by TryTransitionToFailedAsync (its
            // pre-lock terminal check) still returns Working, but a terminal (Completed)
            // state is persisted before the locked apply re-reads it.
            store.ArmTerminalInjectionOnNextRead(TaskInState(taskId, TaskState.Completed));

            releaseThrow.TrySetResult();

            // Wait until the background drain has entered the failure path (the event
            // loop has exited): only then is DisposeAsync safe to await. The failure
            // transition itself runs with CancellationToken.None, so Dispose's
            // cancellation cannot preempt it once we are past the drain loop.
            await store.OuterReadObserved.WaitAsync(TimeSpan.FromSeconds(5));
            await server.DisposeAsync();

            var persisted = await store.GetTaskAsync(taskId);
            Assert.NotNull(persisted);
            Assert.Equal(TaskState.Completed, persisted!.Status.State);
        }

        private static AgentTask TaskInState(string id, TaskState state) => new()
        {
            Id = id,
            ContextId = "issue-401-ctx",
            Status = new TaskStatus { State = state, Timestamp = DateTimeOffset.UtcNow },
        };

        private static async Task WaitForStateAsync(ITaskStore store, string taskId, TaskState expected)
        {
            for (var i = 0; i < 100; i++)
            {
                var task = await store.GetTaskAsync(taskId);
                if (task?.Status.State == expected)
                {
                    return;
                }

                await Task.Delay(5);
            }

            Assert.Fail($"Task {taskId} did not reach {expected}.");
        }

        private sealed class HookAgentHandler : IAgentHandler
        {
            public Func<RequestContext, AgentEventQueue, CancellationToken, Task>? OnExecute { get; set; }

            public Func<RequestContext, AgentEventQueue, CancellationToken, Task>? OnCancel { get; set; }

            public Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
                => OnExecute?.Invoke(context, eventQueue, cancellationToken) ?? Task.CompletedTask;

            public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
                => OnCancel?.Invoke(context, eventQueue, cancellationToken)
                   ?? new TaskUpdater(eventQueue, context.TaskId, context.ContextId).CancelAsync(cancellationToken: cancellationToken).AsTask();
        }

        /// <summary>
        /// In-memory task store that injects a terminal state exactly once, on the next
        /// read, to deterministically reproduce the race window between a writer's
        /// pre-lock terminal check and its locked apply. It signals when that armed read
        /// occurs so a test can await the drain safely afterwards.
        /// </summary>
        private sealed class InjectingTaskStore : ITaskStore
        {
            private readonly InMemoryTaskStore _inner = new();
            private readonly TaskCompletionSource _outerReadObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private AgentTask? _injected;
            private int _armed;

            public Task OuterReadObserved => _outerReadObserved.Task;

            public void ArmTerminalInjectionOnNextRead(AgentTask terminal)
            {
                _injected = terminal;
                Volatile.Write(ref _armed, 1);
            }

            public async Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
            {
                var current = await _inner.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
                if (Interlocked.Exchange(ref _armed, 0) == 1 && _injected is { } terminal)
                {
                    await _inner.SaveTaskAsync(taskId, terminal, cancellationToken).ConfigureAwait(false);
                    _outerReadObserved.TrySetResult();
                }

                return current;
            }

            public Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default)
                => _inner.SaveTaskAsync(taskId, task, cancellationToken);

            public Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
                => _inner.DeleteTaskAsync(taskId, cancellationToken);

            public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
                => _inner.ListTasksAsync(request, cancellationToken);
        }
    }
}

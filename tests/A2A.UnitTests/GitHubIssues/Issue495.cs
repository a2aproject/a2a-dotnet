using Microsoft.Extensions.Logging.Abstractions;

namespace A2A.UnitTests.GitHubIssues
{
    /// <summary>
    /// Regression tests for issue #495: a failure to read or persist authoritative
    /// task state while streaming must surface to the caller, not be converted into a
    /// normal end-of-stream. <c>A2AServer.SendStreamingMessageAsync</c> previously
    /// caught every exception from <c>ApplyEventAsync</c> and <c>yield break</c>'d, so
    /// a task-store failure was indistinguishable from successful completion.
    /// Caller-requested cancellation must still propagate as cancellation rather than
    /// being classified as an error.
    /// </summary>
    public sealed class Issue495
    {
        [Fact]
        public async Task StreamingPersistenceFailureThrowsInsteadOfCompletingNormally()
        {
            var notifier = new ChannelEventNotifier();
            var store = new ThrowOnStateTaskStore(TaskState.Working, new InvalidOperationException("persistence failed"));
            var handler = new HookAgentHandler();
            await using var server = new A2AServer(handler, store, notifier, NullLogger<A2AServer>.Instance);

            // Submitted persists and is streamed to the caller; the Working save then
            // throws, exercising a persistence failure after a response was emitted.
            handler.OnExecute = async (ctx, eq, ct) =>
            {
                var updater = new TaskUpdater(eq, ctx.TaskId, ctx.ContextId);
                await updater.SubmitAsync(cancellationToken: ct).ConfigureAwait(false);
                await updater.StartWorkAsync(cancellationToken: ct).ConfigureAwait(false);
                eq.Complete();
            };

            var request = new SendMessageRequest
            {
                Message = new Message { MessageId = "u1", Parts = [Part.FromText("hi")], Role = Role.User },
            };

            var responsesSeen = 0;
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var response in server.SendStreamingMessageAsync(request).ConfigureAwait(false))
                {
                    responsesSeen++;
                }
            });

            Assert.Equal("persistence failed", ex.Message);
            Assert.True(responsesSeen >= 1, "the pre-failure Submitted response should have been observed");
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
        /// In-memory task store that throws from <see cref="SaveTaskAsync"/> the first
        /// time it is asked to persist a task in a given state, to deterministically
        /// simulate an authoritative persistence failure mid-stream.
        /// </summary>
        private sealed class ThrowOnStateTaskStore : ITaskStore
        {
            private readonly InMemoryTaskStore _inner = new();
            private readonly TaskState _failOn;
            private readonly Exception _failure;

            public ThrowOnStateTaskStore(TaskState failOn, Exception failure)
            {
                _failOn = failOn;
                _failure = failure;
            }

            public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
                => _inner.GetTaskAsync(taskId, cancellationToken);

            public Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default)
            {
                if (task.Status.State == _failOn)
                {
                    throw _failure;
                }

                return _inner.SaveTaskAsync(taskId, task, cancellationToken);
            }

            public Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
                => _inner.DeleteTaskAsync(taskId, cancellationToken);

            public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
                => _inner.ListTasksAsync(request, cancellationToken);
        }
    }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A2A.UnitTests.Server;

public class A2AServerTests
{
    private sealed class TestAgentHandler : IAgentHandler
    {
        public Func<AgentContext, AgentEventQueue, CancellationToken, Task>? OnExecute { get; set; }
        public Func<AgentContext, AgentEventQueue, CancellationToken, Task>? OnCancel { get; set; }

        public Task ExecuteAsync(AgentContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
            => OnExecute?.Invoke(context, eventQueue, cancellationToken) ?? Task.CompletedTask;

        public Task CancelAsync(AgentContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
            => OnCancel?.Invoke(context, eventQueue, cancellationToken)
               ?? new TaskUpdater(eventQueue, context.TaskId, context.ContextId).CancelAsync(cancellationToken).AsTask();
    }

    private static (A2AServer server, InMemoryEventStore store, TestAgentHandler handler)
        CreateServer()
    {
        var notifier = new ChannelEventNotifier();
        var store = new InMemoryEventStore(notifier);
        var subscriber = new ChannelEventSubscriber(store, notifier);
        var handler = new TestAgentHandler();
        var server = new A2AServer(handler, store, subscriber, NullLogger<A2AServer>.Instance);
        return (server, store, handler);
    }

    [Fact]
    public async Task GivenNewMessage_WhenHandlerReturnsMessage_ThenSendMessageReturnsMessageResponse()
    {
        // Arrange
        var (server, _, handler) = CreateServer();
        handler.OnExecute = async (ctx, eq, ct) =>
        {
            await eq.EnqueueMessageAsync(new Message
            {
                Role = Role.Agent,
                MessageId = "m1",
                ContextId = ctx.ContextId,
                Parts = [Part.FromText("Goodbye!")],
            }, ct);
            eq.Complete();
        };

        var request = new SendMessageRequest
        {
            Message = new Message { MessageId = "u1", Parts = [Part.FromText("Hello!")], Role = Role.User }
        };

        // Act
        var result = await server.SendMessageAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Message);
        Assert.Equal("Goodbye!", result.Message!.Parts[0].Text);
    }

    [Fact]
    public async Task GivenNewMessage_WhenHandlerReturnsTask_ThenSendMessageReturnsTaskResponse()
    {
        // Arrange
        var (server, _, handler) = CreateServer();
        handler.OnExecute = async (ctx, eq, ct) =>
        {
            var updater = new TaskUpdater(eq, ctx.TaskId, ctx.ContextId);
            await updater.SubmitAsync(ct);
            await updater.CompleteAsync(cancellationToken: ct);
        };

        var request = new SendMessageRequest
        {
            Message = new Message { MessageId = "u1", Parts = [Part.FromText("Hello!")], Role = Role.User }
        };

        // Act
        var result = await server.SendMessageAsync(request);

        // Assert — task status reflects final state after all events consumed
        Assert.NotNull(result);
        Assert.NotNull(result.Task);
        Assert.Equal(TaskState.Completed, result.Task!.Status.State);
    }

    [Fact]
    public async Task GivenNewMessage_WhenHandlerReturnsTask_ThenTaskPersistedInStore()
    {
        // Arrange
        var (server, store, handler) = CreateServer();
        string? capturedTaskId = null;
        handler.OnExecute = async (ctx, eq, ct) =>
        {
            capturedTaskId = ctx.TaskId;
            var updater = new TaskUpdater(eq, ctx.TaskId, ctx.ContextId);
            await updater.SubmitAsync(ct);
            await updater.CompleteAsync(cancellationToken: ct);
        };

        var request = new SendMessageRequest
        {
            Message = new Message { MessageId = "u1", Parts = [Part.FromText("Hello!")], Role = Role.User }
        };

        // Act
        await server.SendMessageAsync(request);

        // Assert
        Assert.NotNull(capturedTaskId);
        var persisted = await store.GetTaskAsync(capturedTaskId!);
        Assert.NotNull(persisted);
    }

    [Fact]
    public async Task GivenExistingTask_WhenSendMessage_ThenHistoryAppended()
    {
        // Arrange
        var (server, store, handler) = CreateServer();
        var existingTask = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
            History = [new Message { MessageId = "m0", Parts = [Part.FromText("initial")] }],
        };
        await store.AppendAsync(existingTask.Id, new StreamResponse { Task = existingTask });

        handler.OnExecute = async (ctx, eq, ct) =>
        {
            await eq.EnqueueMessageAsync(new Message
            {
                Role = Role.Agent,
                MessageId = "m2",
                ContextId = ctx.ContextId,
                Parts = [Part.FromText("reply")],
            }, ct);
            eq.Complete();
        };

        var request = new SendMessageRequest
        {
            Message = new Message { MessageId = "m1", TaskId = "t1", Parts = [Part.FromText("follow-up")], Role = Role.User }
        };

        // Act
        await server.SendMessageAsync(request);

        // Assert — history should now have 3 messages: initial (m0), user follow-up (m1), agent reply (m2)
        var task = await store.GetTaskAsync("t1");
        Assert.NotNull(task);
        Assert.NotNull(task!.History);
        Assert.Equal(3, task.History.Count);
    }

    [Fact]
    public async Task GivenTerminalTask_WhenSendMessage_ThenThrowsUnsupportedOperation()
    {
        // Arrange
        var (server, store, handler) = CreateServer();
        handler.OnExecute = (_, eq, _) => { eq.Complete(); return Task.CompletedTask; };
        await store.AppendAsync("t1", new StreamResponse { Task = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Completed },
        }});

        var request = new SendMessageRequest
        {
            Message = new Message { MessageId = "u1", TaskId = "t1", Parts = [Part.FromText("hi")], Role = Role.User }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() => server.SendMessageAsync(request));
        Assert.Equal(A2AErrorCode.UnsupportedOperation, ex.ErrorCode);
    }

    [Fact]
    public async Task GivenMissingTaskId_WhenSendMessage_ThenContextIdsGenerated()
    {
        // Arrange
        var (server, _, handler) = CreateServer();
        AgentContext? capturedContext = null;
        handler.OnExecute = async (ctx, eq, ct) =>
        {
            capturedContext = ctx;
            await eq.EnqueueMessageAsync(new Message
            {
                Role = Role.Agent,
                MessageId = "m1",
                Parts = [Part.FromText("ok")],
            }, ct);
            eq.Complete();
        };

        var request = new SendMessageRequest
        {
            Message = new Message { MessageId = "u1", Parts = [Part.FromText("hi")], Role = Role.User }
        };

        // Act
        await server.SendMessageAsync(request);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.False(string.IsNullOrEmpty(capturedContext!.TaskId));
        Assert.False(string.IsNullOrEmpty(capturedContext.ContextId));
        Assert.False(capturedContext.IsContinuation);
    }

    [Fact]
    public async Task GivenExistingTask_WhenCancelTask_ThenHandlerCancelCalled()
    {
        // Arrange
        var (server, store, handler) = CreateServer();
        await store.AppendAsync("t1", new StreamResponse { Task = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
        }});

        bool cancelCalled = false;
        handler.OnCancel = async (ctx, eq, ct) =>
        {
            cancelCalled = true;
            var updater = new TaskUpdater(eq, ctx.TaskId, ctx.ContextId);
            await updater.CancelAsync(ct);
        };

        // Act
        var result = await server.CancelTaskAsync(new CancelTaskRequest { Id = "t1" });

        // Assert
        Assert.True(cancelCalled);
        Assert.Equal(TaskState.Canceled, result.Status.State);
    }

    [Fact]
    public async Task GivenTerminalTask_WhenCancelTask_ThenThrowsTaskNotCancelable()
    {
        // Arrange
        var (server, store, _) = CreateServer();
        await store.AppendAsync("t1", new StreamResponse { Task = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Completed },
        }});

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            server.CancelTaskAsync(new CancelTaskRequest { Id = "t1" }));
        Assert.Equal(A2AErrorCode.TaskNotCancelable, ex.ErrorCode);
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsTask_WhenExists()
    {
        // Arrange
        var (server, store, _) = CreateServer();
        await store.AppendAsync("t1", new StreamResponse { Task = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Submitted }
        }});

        // Act
        var result = await server.GetTaskAsync(new GetTaskRequest { Id = "t1" });

        // Assert
        Assert.NotNull(result);
        Assert.Equal("t1", result.Id);
        Assert.Equal(TaskState.Submitted, result.Status.State);
    }

    [Fact]
    public async Task GetTaskAsync_ThrowsTaskNotFound_WhenNotExists()
    {
        // Arrange
        var (server, _, _) = CreateServer();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            server.GetTaskAsync(new GetTaskRequest { Id = "nonexistent" }));
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task GetTaskAsync_RespectsHistoryLength()
    {
        // Arrange
        var (server, store, _) = CreateServer();
        await store.AppendAsync("t1", new StreamResponse { Task = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
            History = [
                new Message { MessageId = "m1", Parts = [Part.FromText("First")] },
                new Message { MessageId = "m2", Parts = [Part.FromText("Second")] },
                new Message { MessageId = "m3", Parts = [Part.FromText("Third")] },
            ]
        }});

        // Act
        var result = await server.GetTaskAsync(new GetTaskRequest { Id = "t1", HistoryLength = 2 });

        // Assert
        Assert.NotNull(result.History);
        Assert.Equal(2, result.History.Count);
        Assert.Equal("m2", result.History[0].MessageId);
        Assert.Equal("m3", result.History[1].MessageId);
    }

    [Fact]
    public async Task SubscribeToTaskAsync_ThrowsTaskNotFound_WhenNotExists()
    {
        // Arrange
        var (server, _, _) = CreateServer();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in server.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "notfound" }))
            {
            }
        });
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task SubscribeToTaskAsync_ReturnsTaskAsFirstEvent()
    {
        // Arrange
        var (server, store, handler) = CreateServer();
        handler.OnExecute = (_, eq, _) => { eq.Complete(); return Task.CompletedTask; };
        await store.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask
            {
                Id = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Working },
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act — first event from SubscribeToTaskAsync MUST be the Task object (spec §3.1.6)
        StreamResponse? firstEvent = null;
        await foreach (var e in server.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "t1" }, cts.Token))
        {
            firstEvent = e;
            break; // Only need the first event
        }

        // Assert
        Assert.NotNull(firstEvent);
        Assert.NotNull(firstEvent!.Task);
        Assert.Equal("t1", firstEvent.Task!.Id);
    }

    [Fact]
    public async Task SubscribeToTaskAsync_ThrowsUnsupportedOperation_WhenTerminalState()
    {
        // Arrange
        var (server, store, _) = CreateServer();
        await store.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask
            {
                Id = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Completed },
            }
        });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in server.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "t1" }))
            {
            }
        });
        Assert.Equal(A2AErrorCode.UnsupportedOperation, ex.ErrorCode);
    }

    [Fact]
    public async Task ListTasksAsync_DelegatesToStore()
    {
        // Arrange
        var (server, store, _) = CreateServer();
        await store.AppendAsync("t1", new StreamResponse { Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } } });
        await store.AppendAsync("t2", new StreamResponse { Task = new AgentTask { Id = "t2", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Completed } } });

        // Act
        var result = await server.ListTasksAsync(new ListTasksRequest { ContextId = "ctx-1" });

        // Assert
        Assert.NotNull(result.Tasks);
        Assert.Equal(2, result.Tasks!.Count);
    }

    [Fact]
    public async Task PushNotificationConfig_ThrowsNotSupported()
    {
        // Arrange
        var (server, _, _) = CreateServer();

        // Act & Assert
        await Assert.ThrowsAsync<A2AException>(() =>
            server.CreateTaskPushNotificationConfigAsync(new CreateTaskPushNotificationConfigRequest()));
        await Assert.ThrowsAsync<A2AException>(() =>
            server.GetTaskPushNotificationConfigAsync(new GetTaskPushNotificationConfigRequest()));
    }

    [Fact]
    public async Task GetExtendedAgentCard_ThrowsNotConfigured()
    {
        // Arrange
        var (server, _, _) = CreateServer();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            server.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest()));
        Assert.Equal(A2AErrorCode.ExtendedAgentCardNotConfigured, ex.ErrorCode);
    }
}

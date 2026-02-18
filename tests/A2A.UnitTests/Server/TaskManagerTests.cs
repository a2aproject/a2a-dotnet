using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A2A.UnitTests.Server;

public class TaskManagerTests
{
    private static TaskManager CreateTaskManager(ITaskStore? store = null)
    {
        var taskStore = store ?? new InMemoryTaskStore();
        return new TaskManager(taskStore, NullLogger<TaskManager>.Instance);
    }

    [Fact]
    public async Task SendMessageAsync_InvokesOnSendMessageCallback()
    {
        // Arrange
        var taskManager = CreateTaskManager();
        var expectedResponse = new SendMessageResponse
        {
            Message = new Message { Role = Role.Agent, Parts = [Part.FromText("Goodbye!")] }
        };

        taskManager.OnSendMessage = (request, _) =>
        {
            Assert.Equal("Hello!", request.Message.Parts[0].Text);
            return Task.FromResult(expectedResponse);
        };

        var sendRequest = new SendMessageRequest
        {
            Message = new Message
            {
                Parts = [Part.FromText("Hello!")],
                Role = Role.User,
            }
        };

        // Act
        var result = await taskManager.SendMessageAsync(sendRequest);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Message);
        Assert.Equal("Goodbye!", result.Message!.Parts[0].Text);
    }

    [Fact]
    public async Task SendMessageAsync_ThrowsIfNoCallbackSet()
    {
        // Arrange
        var taskManager = CreateTaskManager();
        var sendRequest = new SendMessageRequest
        {
            Message = new Message { Parts = [Part.FromText("Hello!")], Role = Role.User }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() => taskManager.SendMessageAsync(sendRequest));
        Assert.Equal(A2AErrorCode.UnsupportedOperation, ex.ErrorCode);
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsTask_WhenExists()
    {
        // Arrange
        var store = new InMemoryTaskStore();
        var task = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Submitted }
        };
        await store.SetTaskAsync(task);

        var taskManager = CreateTaskManager(store);

        // Act
        var result = await taskManager.GetTaskAsync(new GetTaskRequest { Id = "t1" });

        // Assert
        Assert.NotNull(result);
        Assert.Equal("t1", result.Id);
        Assert.Equal(TaskState.Submitted, result.Status.State);
    }

    [Fact]
    public async Task GetTaskAsync_ThrowsTaskNotFound_WhenNotExists()
    {
        // Arrange
        var taskManager = CreateTaskManager();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            taskManager.GetTaskAsync(new GetTaskRequest { Id = "nonexistent" }));
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task GetTaskAsync_RespectsHistoryLength()
    {
        // Arrange
        var store = new InMemoryTaskStore();
        var task = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
            History = [
                new Message { MessageId = "m1", Parts = [Part.FromText("First")] },
                new Message { MessageId = "m2", Parts = [Part.FromText("Second")] },
                new Message { MessageId = "m3", Parts = [Part.FromText("Third")] },
            ]
        };
        await store.SetTaskAsync(task);

        var taskManager = CreateTaskManager(store);

        // Act
        var result = await taskManager.GetTaskAsync(new GetTaskRequest { Id = "t1", HistoryLength = 2 });

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.History);
        Assert.Equal(2, result.History.Count);
        Assert.Equal("m2", result.History[0].MessageId);
        Assert.Equal("m3", result.History[1].MessageId);
    }

    [Fact]
    public async Task CancelTaskAsync_InvokesOnCancelTaskCallback()
    {
        // Arrange
        var store = new InMemoryTaskStore();
        var task = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Submitted }
        };
        await store.SetTaskAsync(task);

        var taskManager = CreateTaskManager(store);
        taskManager.OnCancelTask = async (request, ct) =>
        {
            var updated = await store.UpdateStatusAsync(request.Id, new TaskStatus { State = TaskState.Canceled }, ct);
            return updated;
        };

        // Act
        var result = await taskManager.CancelTaskAsync(new CancelTaskRequest { Id = "t1" });

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TaskState.Canceled, result.Status.State);
    }

    [Fact]
    public async Task CancelTaskAsync_ThrowsIfNoCallbackSet()
    {
        // Arrange
        var taskManager = CreateTaskManager();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            taskManager.CancelTaskAsync(new CancelTaskRequest { Id = "t1" }));
        Assert.Equal(A2AErrorCode.TaskNotCancelable, ex.ErrorCode);
    }

    [Fact]
    public void SendStreamingMessageAsync_ThrowsIfNoCallbackSet()
    {
        // Arrange
        var taskManager = CreateTaskManager();
        var sendRequest = new SendMessageRequest
        {
            Message = new Message { Parts = [Part.FromText("Hello!")], Role = Role.User }
        };

        // Act & Assert
        Assert.Throws<A2AException>(() => taskManager.SendStreamingMessageAsync(sendRequest));
    }

    [Fact]
    public async Task SubscribeToTaskAsync_ThrowsTaskNotFound_WhenNotExists()
    {
        // Arrange
        var taskManager = CreateTaskManager();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in taskManager.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "notfound" }))
            {
            }
        });
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task ListTasksAsync_DelegatesToStore()
    {
        // Arrange
        var store = new InMemoryTaskStore();
        await store.SetTaskAsync(new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } });
        await store.SetTaskAsync(new AgentTask { Id = "t2", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Completed } });

        var taskManager = CreateTaskManager(store);

        // Act
        var result = await taskManager.ListTasksAsync(new ListTasksRequest { ContextId = "ctx-1" });

        // Assert
        Assert.NotNull(result.Tasks);
        Assert.Equal(2, result.Tasks!.Count);
    }

    [Fact]
    public async Task PushNotificationConfig_ThrowsNotSupported()
    {
        // Arrange
        var taskManager = CreateTaskManager();

        // Act & Assert
        await Assert.ThrowsAsync<A2AException>(() =>
            taskManager.CreateTaskPushNotificationConfigAsync(new CreateTaskPushNotificationConfigRequest()));
        await Assert.ThrowsAsync<A2AException>(() =>
            taskManager.GetTaskPushNotificationConfigAsync(new GetTaskPushNotificationConfigRequest()));
    }

    [Fact]
    public async Task GetExtendedAgentCard_ThrowsNotConfigured()
    {
        // Arrange
        var taskManager = CreateTaskManager();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            taskManager.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest()));
        Assert.Equal(A2AErrorCode.ExtendedAgentCardNotConfigured, ex.ErrorCode);
    }
}

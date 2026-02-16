namespace A2A.UnitTests.Server;

public class InMemoryTaskStoreTests
{
    [Fact]
    public async Task SetTaskAsync_And_GetTaskAsync_ShouldStoreAndRetrieveTask()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask { Id = "task1", Status = new TaskStatus { State = TaskState.Submitted } };

        // Act
        var stored = await sut.SetTaskAsync(task);
        var result = await sut.GetTaskAsync("task1");

        // Assert
        Assert.NotNull(stored);
        Assert.Equal("task1", stored.Id);
        Assert.NotNull(result);
        Assert.Equal("task1", result!.Id);
        Assert.Equal(TaskState.Submitted, result.Status.State);
    }

    [Fact]
    public async Task GetTaskAsync_ShouldReturnNull_WhenTaskDoesNotExist()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        // Act
        var result = await sut.GetTaskAsync("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldUpdateTaskStatus()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask { Id = "task2", Status = new TaskStatus { State = TaskState.Submitted } };
        await sut.SetTaskAsync(task);
        var newStatus = new TaskStatus { State = TaskState.Working, Message = new Message { MessageId = "msg1" } };

        // Act
        var updatedTask = await sut.UpdateStatusAsync("task2", newStatus);

        // Assert
        Assert.Equal(TaskState.Working, updatedTask.Status.State);
        Assert.Equal("msg1", updatedTask.Status.Message!.MessageId);

        var retrievedTask = await sut.GetTaskAsync("task2");
        Assert.Equal(TaskState.Working, retrievedTask!.Status.State);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldThrow_WhenTaskDoesNotExist()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            sut.UpdateStatusAsync("notfound", new TaskStatus { State = TaskState.Completed }));
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task AppendHistoryAsync_ShouldAppendMessage()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask { Id = "task3", Status = new TaskStatus { State = TaskState.Submitted } };
        await sut.SetTaskAsync(task);

        var message = new Message { MessageId = "msg1", Parts = [Part.FromText("hello")] };

        // Act
        var updatedTask = await sut.AppendHistoryAsync("task3", message);

        // Assert
        Assert.NotNull(updatedTask.History);
        Assert.Single(updatedTask.History);
        Assert.Equal("msg1", updatedTask.History[0].MessageId);
    }

    [Fact]
    public async Task AppendHistoryAsync_ShouldThrow_WhenTaskDoesNotExist()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            sut.AppendHistoryAsync("notfound", new Message { MessageId = "msg1" }));
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task ListTasksAsync_ShouldReturnAllTasks()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        await sut.SetTaskAsync(new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } });
        await sut.SetTaskAsync(new AgentTask { Id = "t2", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Working } });
        await sut.SetTaskAsync(new AgentTask { Id = "t3", ContextId = "ctx-2", Status = new TaskStatus { State = TaskState.Completed } });

        // Act
        var result = await sut.ListTasksAsync(new ListTasksRequest());

        // Assert
        Assert.NotNull(result.Tasks);
        Assert.Equal(3, result.Tasks!.Count);
        Assert.Equal(3, result.TotalSize);
    }

    [Fact]
    public async Task ListTasksAsync_ShouldFilterByContextId()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        await sut.SetTaskAsync(new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } });
        await sut.SetTaskAsync(new AgentTask { Id = "t2", ContextId = "ctx-2", Status = new TaskStatus { State = TaskState.Working } });

        // Act
        var result = await sut.ListTasksAsync(new ListTasksRequest { ContextId = "ctx-1" });

        // Assert
        Assert.NotNull(result.Tasks);
        Assert.Single(result.Tasks!);
        Assert.Equal("t1", result.Tasks![0].Id);
    }

    [Fact]
    public async Task ListTasksAsync_ShouldFilterByStatus()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        await sut.SetTaskAsync(new AgentTask { Id = "t1", Status = new TaskStatus { State = TaskState.Submitted } });
        await sut.SetTaskAsync(new AgentTask { Id = "t2", Status = new TaskStatus { State = TaskState.Completed } });

        // Act
        var result = await sut.ListTasksAsync(new ListTasksRequest { Status = TaskState.Completed });

        // Assert
        Assert.NotNull(result.Tasks);
        Assert.Single(result.Tasks!);
        Assert.Equal("t2", result.Tasks![0].Id);
    }
}

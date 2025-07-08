namespace A2A.UnitTests.Server;

public class InMemoryTaskStoreTests
{
    [Fact]
    public async Task SetTaskAsync_And_GetTaskAsync_ShouldStoreAndRetrieveTask()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };

        // Act
        await sut.SetTaskAsync(task);
        var result = await sut.GetTaskAsync("task1");

        // Assert
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
        var task = new AgentTask { Id = "task2", Status = new AgentTaskStatus { State = TaskState.Submitted } };
        await sut.SetTaskAsync(task);
        var message = new Message { MessageId = "msg1" };

        // Act
        var status = await sut.UpdateStatusAsync("task2", TaskState.Working, message);
        var updatedTask = await sut.GetTaskAsync("task2");

        // Assert
        Assert.Equal(TaskState.Working, status.State);
        Assert.Equal(TaskState.Working, updatedTask!.Status.State);
        Assert.Equal("msg1", status.Message!.MessageId);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldThrow_WhenTaskDoesNotExist()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => sut.UpdateStatusAsync("notfound", TaskState.Completed));
    }

    [Fact]
    public async Task SetPushNotificationConfigAsync_And_GetPushNotificationAsync_ShouldStoreAndRetrieveConfig()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var config = new TaskPushNotificationConfig { TaskId = "task3", PushNotificationConfig = new PushNotificationConfig { Url = "http://test" } };

        // Act
        await sut.SetPushNotificationConfigAsync(config);
        var result = await sut.GetPushNotificationAsync("task3");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("task3", result!.TaskId);
        Assert.Equal("http://test", result.PushNotificationConfig.Url);
    }

    [Fact]
    public async Task GetPushNotificationAsync_ShouldReturnNull_WhenConfigDoesNotExist()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        // Act
        var result = await sut.GetPushNotificationAsync("missing");

        // Assert
        Assert.Null(result);
    }
}
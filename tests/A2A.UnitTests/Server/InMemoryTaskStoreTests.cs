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
    public async Task GetPushNotificationAsync_ShouldReturnNull_WhenTaskDoesNotExist()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        // Act
        var result = await sut.GetPushNotificationAsync("missing", "config-missing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPushNotificationAsync_ShouldReturnNull_WhenConfigDoesNotExist()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        await sut.SetPushNotificationConfigAsync(new TaskPushNotificationConfig { TaskId = "task-id", PushNotificationConfig = new() { Id = "config-id" } });

        // Act
        var result = await sut.GetPushNotificationAsync("task-id", "config-missing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPushNotificationAsync_ShouldReturnCorrectConfig_WhenMultipleConfigsExistForSameTask()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var taskId = "task-with-multiple-configs";

        var config1 = new TaskPushNotificationConfig
        {
            TaskId = taskId,
            PushNotificationConfig = new PushNotificationConfig
            {
                Url = "http://config1",
                Id = "config-id-1",
                Token = "token1"
            }
        };

        var config2 = new TaskPushNotificationConfig
        {
            TaskId = taskId,
            PushNotificationConfig = new PushNotificationConfig
            {
                Url = "http://config2",
                Id = "config-id-2",
                Token = "token2"
            }
        };

        var config3 = new TaskPushNotificationConfig
        {
            TaskId = taskId,
            PushNotificationConfig = new PushNotificationConfig
            {
                Url = "http://config3",
                Id = "config-id-3",
                Token = "token3"
            }
        };

        // Act - Store multiple configs for the same task
        await sut.SetPushNotificationConfigAsync(config1);
        await sut.SetPushNotificationConfigAsync(config2);
        await sut.SetPushNotificationConfigAsync(config3);

        // Get specific configs by both taskId and notificationConfigId
        var result1 = await sut.GetPushNotificationAsync(taskId, "config-id-1");
        var result2 = await sut.GetPushNotificationAsync(taskId, "config-id-2");
        var result3 = await sut.GetPushNotificationAsync(taskId, "config-id-3");
        var resultNotFound = await sut.GetPushNotificationAsync(taskId, "non-existent-config");

        // Assert - Verify each call returns the correct specific config
        Assert.NotNull(result1);
        Assert.Equal(taskId, result1!.TaskId);
        Assert.Equal("config-id-1", result1.PushNotificationConfig.Id);
        Assert.Equal("http://config1", result1.PushNotificationConfig.Url);
        Assert.Equal("token1", result1.PushNotificationConfig.Token);

        Assert.NotNull(result2);
        Assert.Equal(taskId, result2!.TaskId);
        Assert.Equal("config-id-2", result2.PushNotificationConfig.Id);
        Assert.Equal("http://config2", result2.PushNotificationConfig.Url);
        Assert.Equal("token2", result2.PushNotificationConfig.Token);

        Assert.NotNull(result3);
        Assert.Equal(taskId, result3!.TaskId);
        Assert.Equal("config-id-3", result3.PushNotificationConfig.Id);
        Assert.Equal("http://config3", result3.PushNotificationConfig.Url);
        Assert.Equal("token3", result3.PushNotificationConfig.Token);

        Assert.Null(resultNotFound);
    }

    [Fact]
    public async Task GetPushNotificationsAsync_ShouldReturnEmptyList_WhenNoConfigsExistForTask()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        // Act
        var result = await sut.GetPushNotificationsAsync("task-without-configs");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
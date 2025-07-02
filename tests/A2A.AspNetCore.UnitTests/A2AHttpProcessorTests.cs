using Microsoft.Extensions.Logging.Abstractions;

namespace A2A.AspNetCore.Tests;

public class A2AHttpProcessorTests
{

    [Fact]
    public async Task GetAgentCard_ShouldReturnNotNull()
    {
        // Arrange
        var taskManager = new TaskManager();
        var logger = NullLogger.Instance;

        // Act
        var result = await A2AHttpProcessor.GetAgentCard(taskManager, logger, "http://example.com");

        // Assert
        Assert.NotNull(result);
    }
    [Fact]
    public async Task GetTask_ShouldReturnNotNull()
    {
        // Arrange
        var taskStore = new InMemoryTaskStore();
        await taskStore.SetTaskAsync(new AgentTask
        {
            Id = "testId",
        });
        var taskManager = new TaskManager(taskStore: taskStore);
        var logger = NullLogger.Instance;
        var id = "testId";
        var historyLength = 10;

        // Act
        var result = await A2AHttpProcessor.GetTask(taskManager, logger, id, historyLength, null);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<A2AResponseResult>(result);
    }

    [Fact]
    public async Task CancelTask_ShouldReturnNotNull()
    {
        // Arrange
        var taskStore = new InMemoryTaskStore();
        await taskStore.SetTaskAsync(new AgentTask
        {
            Id = "testId",
        });
        var taskManager = new TaskManager(taskStore: taskStore);
        var logger = NullLogger.Instance;
        var id = "testId";

        // Act
        var result = await A2AHttpProcessor.CancelTask(taskManager, logger, id);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<A2AResponseResult>(result);
    }

    [Fact]
    public async Task SendTaskMessage_ShouldReturnNotNull()
    {
        // Arrange
        var taskStore = new InMemoryTaskStore();
        await taskStore.SetTaskAsync(new AgentTask
        {
            Id = "testId",
        });
        var taskManager = new TaskManager(taskStore: taskStore);
        var logger = NullLogger.Instance;
        var id = "testId";
        var sendParams = new MessageSendParams();
        var historyLength = 10;

        // Act
        var result = await A2AHttpProcessor.SendTaskMessage(taskManager, logger, id, sendParams, historyLength, null);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<A2AResponseResult>(result);
    }

}
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace A2A.AspNetCore.Tests;

public class A2AHttpProcessorTests
{
    [Fact]
    public void GetAgentCard_ShouldReturnNotNull()
    {
        // Arrange
        var taskManager = new TaskManager();
        var logger = NullLogger.Instance;

        // Act
        var result = A2AHttpProcessor.GetAgentCard(taskManager, logger, "http://example.com");

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
        var sendParams = new MessageSendParams
        {
            Message = { TaskId = "testId" },
            Configuration = new() { HistoryLength = 10 }
        };

        // Act
        var result = await A2AHttpProcessor.SendMessage(taskManager, logger, sendParams);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<A2AResponseResult>(result);
    }

    [Theory]
    [InlineData(A2AErrorCode.TaskNotFound, StatusCodes.Status404NotFound)]
    [InlineData(A2AErrorCode.MethodNotFound, StatusCodes.Status404NotFound)]
    [InlineData(A2AErrorCode.InvalidRequest, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.InvalidParams, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.TaskNotCancelable, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.UnsupportedOperation, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.ParseError, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.PushNotificationNotSupported, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.ContentTypeNotSupported, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(A2AErrorCode.InternalError, StatusCodes.Status500InternalServerError)]
    public async Task GetTask_WithA2AException_ShouldMapToCorrectHttpStatusCode(A2AErrorCode errorCode, int expectedStatusCode)
    {
        // Arrange
        var mockTaskStore = new Mock<ITaskStore>();
        mockTaskStore
            .Setup(ts => ts.GetTaskAsync(It.IsAny<string>()))
            .ThrowsAsync(new A2AException("Test exception", errorCode));

        var taskManager = new TaskManager(taskStore: mockTaskStore.Object);
        var logger = NullLogger.Instance;
        var id = "testId";
        var historyLength = 10;

        // Act
        var result = await A2AHttpProcessor.GetTask(taskManager, logger, id, historyLength, null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedStatusCode, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task GetTask_WithUnknownA2AErrorCode_ShouldReturn500InternalServerError()
    {
        // Arrange
        var mockTaskStore = new Mock<ITaskStore>();
        // Create an A2AException with an unknown/invalid error code by casting an integer that doesn't correspond to any enum value
        var unknownErrorCode = (A2AErrorCode)(-99999);
        mockTaskStore
            .Setup(ts => ts.GetTaskAsync(It.IsAny<string>()))
            .ThrowsAsync(new A2AException("Test exception with unknown error code", unknownErrorCode));

        var taskManager = new TaskManager(taskStore: mockTaskStore.Object);
        var logger = NullLogger.Instance;
        var id = "testId";
        var historyLength = 10;

        // Act
        var result = await A2AHttpProcessor.GetTask(taskManager, logger, id, historyLength, null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, ((IStatusCodeHttpResult)result).StatusCode);
    }
}
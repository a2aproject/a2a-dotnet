using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;

namespace A2A.AspNetCore.Tests;

public class A2AHttpProcessorTests
{
    private sealed class TestAgentHandler : IAgentHandler
    {
        public async Task ExecuteAsync(AgentContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            // For SendMessage: return the existing task if continuation, or a message
            if (context.IsContinuation)
            {
                await eventQueue.EnqueueTaskAsync(context.Task!, cancellationToken);
            }
            else
            {
                await eventQueue.EnqueueMessageAsync(new Message
                {
                    Role = Role.Agent,
                    MessageId = Guid.NewGuid().ToString(),
                    Parts = [Part.FromText("ok")],
                }, cancellationToken);
            }

            eventQueue.Complete();
        }

        public async Task CancelAsync(AgentContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
            await updater.CancelAsync(cancellationToken);
        }
    }

    private static (A2AServer requestHandler, InMemoryEventStore store) CreateServer()
    {
        var store = new InMemoryEventStore();
        var handler = new TestAgentHandler();
        return (new A2AServer(handler, store, NullLogger<A2AServer>.Instance), store);
    }

    [Fact]
    public async Task GetTask_ShouldReturnNotNull()
    {
        // Arrange
        var (requestHandler, store) = CreateServer();
        var agentTask = new AgentTask
        {
            Id = "testId",
            ContextId = "ctx-1",
        };
        await store.AppendAsync(agentTask.Id, new StreamResponse { Task = agentTask });
        var logger = NullLogger.Instance;

        // Act
        var result = await A2AHttpProcessor.GetTaskAsync(requestHandler, logger, "testId", 10, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<JsonRpcResponseResult>(result);
    }

    [Fact]
    public async Task CancelTask_ShouldReturnNotNull()
    {
        // Arrange
        var (requestHandler, store) = CreateServer();
        var agentTask = new AgentTask
        {
            Id = "testId",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Submitted },
        };
        await store.AppendAsync(agentTask.Id, new StreamResponse { Task = agentTask });
        var logger = NullLogger.Instance;

        // Act
        var result = await A2AHttpProcessor.CancelTaskAsync(requestHandler, logger, "testId", CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<JsonRpcResponseResult>(result);
    }

    [Fact]
    public async Task SendTaskMessage_ShouldReturnNotNull()
    {
        // Arrange
        var (requestHandler, store) = CreateServer();
        var agentTask = new AgentTask
        {
            Id = "testId",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Submitted },
        };
        await store.AppendAsync(agentTask.Id, new StreamResponse { Task = agentTask });
        var logger = NullLogger.Instance;
        var sendRequest = new SendMessageRequest
        {
            Message = new Message
            {
                TaskId = "testId",
                Role = Role.User,
                Parts = [Part.FromText("hi")],
            },
            Configuration = new SendMessageConfiguration { HistoryLength = 10 }
        };

        // Act
        var result = await A2AHttpProcessor.SendMessageAsync(requestHandler, logger, sendRequest, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<JsonRpcResponseResult>(result);
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
        var mockTaskStore = new Mock<ITaskEventStore>();
        mockTaskStore
            .Setup(ts => ts.GetTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new A2AException("Test exception", errorCode));

        var handler = new Mock<IAgentHandler>().Object;
        var requestHandler = new A2AServer(handler, mockTaskStore.Object, NullLogger<A2AServer>.Instance);
        var logger = NullLogger.Instance;
        var id = "testId";
        var historyLength = 10;

        // Act
        var result = await A2AHttpProcessor.GetTaskAsync(requestHandler, logger, id, historyLength, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedStatusCode, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task GetTask_WithUnknownA2AErrorCode_ShouldReturn500InternalServerError()
    {
        // Arrange
        var mockTaskStore = new Mock<ITaskEventStore>();
        // Create an A2AException with an unknown/invalid error code by casting an integer that doesn't correspond to any enum value
        var unknownErrorCode = (A2AErrorCode)(-99999);
        mockTaskStore
            .Setup(ts => ts.GetTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new A2AException("Test exception with unknown error code", unknownErrorCode));

        var handler = new Mock<IAgentHandler>().Object;
        var requestHandler = new A2AServer(handler, mockTaskStore.Object, NullLogger<A2AServer>.Instance);
        var logger = NullLogger.Instance;
        var id = "testId";
        var historyLength = 10;

        // Act
        var result = await A2AHttpProcessor.GetTaskAsync(requestHandler, logger, id, historyLength, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, ((IStatusCodeHttpResult)result).StatusCode);
    }
}

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
        public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
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

        public async Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
            await updater.CancelAsync(cancellationToken: cancellationToken);
        }
    }

    private static (A2AServer requestHandler, InMemoryTaskStore store) CreateServer()
    {
        var notifier = new ChannelEventNotifier();
        var store = new InMemoryTaskStore();
        var handler = new TestAgentHandler();
        return (new A2AServer(handler, store, notifier, NullLogger<A2AServer>.Instance), store);
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
        await store.SaveTaskAsync(agentTask.Id, agentTask);
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
        await store.SaveTaskAsync(agentTask.Id, agentTask);
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
        await store.SaveTaskAsync(agentTask.Id, agentTask);
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

    [Fact]
    public async Task SendMessageRestAsync_ClearsTenantFieldsBeforeDispatch()
    {
        var requestHandler = new Mock<IA2ARequestHandler>(MockBehavior.Strict);
        requestHandler
            .Setup(handler => handler.SendMessageAsync(
                It.Is<SendMessageRequest>(request =>
                    request.Tenant == null &&
                    request.Configuration!.TaskPushNotificationConfig!.Tenant == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMessageResponse
            {
                Message = new Message { MessageId = "response", Role = Role.Agent, Parts = [] },
            });
        var request = CreateTenantBearingSendRequest();

        var result = await A2AHttpProcessor.SendMessageRestAsync(
            requestHandler.Object, NullLogger.Instance, request, CancellationToken.None);

        Assert.IsType<A2AResponseResult>(result);
        requestHandler.VerifyAll();
    }

    [Fact]
    public void SendMessageStreamRest_ClearsTenantFieldsBeforeDispatch()
    {
        var requestHandler = new Mock<IA2ARequestHandler>(MockBehavior.Strict);
        requestHandler
            .Setup(handler => handler.SendStreamingMessageAsync(
                It.Is<SendMessageRequest>(request =>
                    request.Tenant == null &&
                    request.Configuration!.TaskPushNotificationConfig!.Tenant == null),
                It.IsAny<CancellationToken>()))
            .Returns(SingleStreamResponse());
        var request = CreateTenantBearingSendRequest();

        var result = A2AHttpProcessor.SendMessageStreamRest(
            requestHandler.Object, NullLogger.Instance, request, CancellationToken.None);

        Assert.IsType<A2AEventStreamResult>(result);
        requestHandler.VerifyAll();
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
    [InlineData(A2AErrorCode.ContentTypeNotSupported, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.ExtendedAgentCardNotConfigured, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.ExtensionSupportRequired, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.VersionNotSupported, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.InvalidAgentResponse, StatusCodes.Status500InternalServerError)]
    [InlineData(A2AErrorCode.InternalError, StatusCodes.Status500InternalServerError)]
    public async Task GetTask_WithA2AException_ShouldMapToCorrectHttpStatusCode(A2AErrorCode errorCode, int expectedStatusCode)
    {
        // Arrange
        var mockTaskStore = new Mock<ITaskStore>();
        mockTaskStore
            .Setup(ts => ts.GetTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new A2AException("Test exception", errorCode));

        var handler = new Mock<IAgentHandler>().Object;
        var notifier = new ChannelEventNotifier();
        var requestHandler = new A2AServer(handler, mockTaskStore.Object, notifier, NullLogger<A2AServer>.Instance);
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
        var mockTaskStore = new Mock<ITaskStore>();
        // Create an A2AException with an unknown/invalid error code by casting an integer that doesn't correspond to any enum value
        var unknownErrorCode = (A2AErrorCode)(-99999);
        mockTaskStore
            .Setup(ts => ts.GetTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new A2AException("Test exception with unknown error code", unknownErrorCode));

        var handler = new Mock<IAgentHandler>().Object;
        var notifier = new ChannelEventNotifier();
        var requestHandler = new A2AServer(handler, mockTaskStore.Object, notifier, NullLogger<A2AServer>.Instance);
        var logger = NullLogger.Instance;
        var id = "testId";
        var historyLength = 10;

        // Act
        var result = await A2AHttpProcessor.GetTaskAsync(requestHandler, logger, id, historyLength, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("TASK_STATE_999")]
    public async Task ListTasks_WithUndefinedNumericStatus_ShouldReturnInvalidParams(string status)
    {
        var requestHandler = new Mock<IA2ARequestHandler>(MockBehavior.Strict);

        var result = await A2AHttpProcessor.ListTasksRestAsync(
            requestHandler.Object,
            NullLogger.Instance,
            null,
            status,
            null,
            null,
            null,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        requestHandler.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Submitted")]
    [InlineData("InputRequired")]
    [InlineData("AuthRequired")]
    public async Task ListTasks_WithCSharpEnumName_ShouldReturnInvalidParams(string status)
    {
        var requestHandler = new Mock<IA2ARequestHandler>(MockBehavior.Strict);

        var result = await A2AHttpProcessor.ListTasksRestAsync(
            requestHandler.Object,
            NullLogger.Instance,
            null,
            status,
            null,
            null,
            null,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        requestHandler.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("TASK_STATE_INPUT_REQUIRED", TaskState.InputRequired)]
    [InlineData("TASK_STATE_AUTH_REQUIRED", TaskState.AuthRequired)]
    public async Task ListTasks_WithMultiWordWireStatus_ShouldPassParsedStatusToHandler(
        string status, TaskState expectedState)
    {
        var requestHandler = new Mock<IA2ARequestHandler>(MockBehavior.Strict);
        requestHandler
            .Setup(handler => handler.ListTasksAsync(
                It.Is<ListTasksRequest>(request => request.Status == expectedState),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListTasksResponse { Tasks = [], NextPageToken = "" });

        var result = await A2AHttpProcessor.ListTasksRestAsync(
            requestHandler.Object,
            NullLogger.Instance,
            null,
            status,
            null,
            null,
            null,
            null,
            null,
            CancellationToken.None);

        Assert.IsType<A2AResponseResult>(result);
        requestHandler.VerifyAll();
    }

    private static SendMessageRequest CreateTenantBearingSendRequest() => new()
    {
        Tenant = "top-level-tenant",
        Message = new Message { MessageId = "m-1", Role = Role.User, Parts = [Part.FromText("hi")] },
        Configuration = new SendMessageConfiguration
        {
            TaskPushNotificationConfig = new TaskPushNotificationConfig
            {
                Url = "https://push.example",
                Tenant = "embedded-tenant",
            },
        },
    };

    private static async IAsyncEnumerable<StreamResponse> SingleStreamResponse()
    {
        await Task.Yield();
        yield return new StreamResponse
        {
            Message = new Message { MessageId = "response", Role = Role.Agent, Parts = [] },
        };
    }
}

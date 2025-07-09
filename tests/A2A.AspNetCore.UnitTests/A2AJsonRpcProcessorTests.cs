using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace A2A.AspNetCore.Tests;

public class A2AJsonRpcProcessorTests
{
    [Fact]
    public async Task ProcessRequest_SingleResponse_MessageSend_Works()
    {
        // Arrange
        var taskManager = new TaskManager();
        var sendParams = new MessageSendParams
        {
            Message = new Message { MessageId = "test-message-id", Parts = [new TextPart { Text = "hi" }] }
        };
        var req = new JsonRpcRequest
        {
            Id = "1",
            Method = A2AMethods.MessageSend,
            Params = ToJsonElement(sendParams)
        };

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequest(taskManager, req);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);

        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);

        Assert.NotNull(BodyContent.Result);
        var agentTask = JsonSerializer.Deserialize<AgentTask>(BodyContent.Result, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(agentTask);
        Assert.Equal(TaskState.Submitted, agentTask.Status.State);
        Assert.NotEmpty(agentTask.History);
        Assert.Equal(MessageRole.User, agentTask.History[0].Role);
        Assert.Equal("hi", ((TextPart)agentTask.History[0].Parts[0]).Text);
        Assert.Equal("test-message-id", agentTask.History[0].MessageId);
    }

    [Fact]
    public async Task ProcessRequest_SingleResponse_InvalidParams_ReturnsError()
    {
        // Arrange
        var taskManager = new TaskManager();
        var req = new JsonRpcRequest
        {
            Id = "2",
            Method = A2AMethods.MessageSend,
            Params = null
        };

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequest(taskManager, req);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);

        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode);
        Assert.Equal("application/json", ContentType);

        Assert.NotNull(BodyContent);
        Assert.Null(BodyContent.Result);

        Assert.NotNull(BodyContent.Error);
        Assert.Equal(-32602, BodyContent.Error!.Code); // Invalid params
        Assert.Equal("Invalid parameters", BodyContent.Error.Message);
    }

    [Fact]
    public async Task SingleResponse_TaskGet_Works()
    {
        // Arrange
        var taskManager = new TaskManager();
        var task = await taskManager.CreateTaskAsync();

        var queryParams = new TaskQueryParams { Id = task.Id };

        // Act
        var result = await A2AJsonRpcProcessor.SingleResponse(taskManager, "4", A2AMethods.TaskGet, ToJsonElement(queryParams));

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);

        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);
        Assert.NotNull(BodyContent);

        var agentTask = JsonSerializer.Deserialize<AgentTask>(BodyContent.Result, A2AJsonUtilities.DefaultOptions);
        Assert.NotNull(agentTask);
        Assert.Equal(TaskState.Submitted, agentTask.Status.State);
        Assert.Empty(agentTask.History);
    }

    [Fact]
    public async Task SingleResponse_TaskCancel_Works()
    {
        // Arrange
        var taskManager = new TaskManager();
        var newTask = await taskManager.CreateTaskAsync();
        var cancelParams = new TaskIdParams { Id = newTask.Id };

        // Act
        var result = await A2AJsonRpcProcessor.SingleResponse(taskManager, "5", A2AMethods.TaskCancel, ToJsonElement(cancelParams));

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);

        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);
        Assert.NotNull(BodyContent);

        var agentTask = JsonSerializer.Deserialize<AgentTask>(BodyContent.Result, A2AJsonUtilities.DefaultOptions);
        Assert.NotNull(agentTask);
        Assert.Equal(TaskState.Canceled, agentTask.Status.State);
        Assert.Empty(agentTask.History);
    }

    [Fact]
    public async Task SingleResponse_TaskPushNotificationConfigSet_Works()
    {
        // Arrange
        var taskManager = new TaskManager();
        var config = new TaskPushNotificationConfig
        {
            TaskId = "test-task",
            PushNotificationConfig = new PushNotificationConfig()
            {
                Url = "https://example.com/notify",
            }
        };

        // Act
        var result = await A2AJsonRpcProcessor.SingleResponse(taskManager, "6", A2AMethods.TaskPushNotificationConfigSet, ToJsonElement(config));

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);

        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);
        Assert.NotNull(BodyContent);

        var notificationConfig = JsonSerializer.Deserialize<TaskPushNotificationConfig>(BodyContent.Result, A2AJsonUtilities.DefaultOptions);
        Assert.NotNull(notificationConfig);

        Assert.Equal("test-task", notificationConfig.TaskId);
        Assert.Equal("https://example.com/notify", notificationConfig.PushNotificationConfig.Url);
    }

    [Fact]
    public async Task SingleResponse_TaskPushNotificationConfigGet_Works()
    {
        // Arrange
        var taskManager = new TaskManager();
        var config = new TaskPushNotificationConfig
        {
            TaskId = "test-task",
            PushNotificationConfig = new PushNotificationConfig()
            {
                Url = "https://example.com/notify",
            }
        };
        await taskManager.SetPushNotificationAsync(config);
        var getParams = new TaskIdParams { Id = "test-task" };

        // Act
        var result = await A2AJsonRpcProcessor.SingleResponse(taskManager, "7", A2AMethods.TaskPushNotificationConfigGet, ToJsonElement(getParams));

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);

        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);
        Assert.NotNull(BodyContent);

        var notificationConfig = JsonSerializer.Deserialize<TaskPushNotificationConfig>(BodyContent.Result, A2AJsonUtilities.DefaultOptions);
        Assert.NotNull(notificationConfig);

        Assert.Equal("test-task", notificationConfig.TaskId);
        Assert.Equal("https://example.com/notify", notificationConfig.PushNotificationConfig.Url);
    }

    [Fact]
    public async Task StreamResponse_MessageStream_InvalidParams_ReturnsError()
    {
        // Arrange
        var taskManager = new TaskManager();

        // Act
        var result = await A2AJsonRpcProcessor.StreamResponse(taskManager, "10", A2AMethods.MessageStream, null);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);

        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode);
        Assert.Equal("application/json", ContentType);

        Assert.NotNull(BodyContent);
        Assert.Null(BodyContent.Result);

        Assert.NotNull(BodyContent.Error);
        Assert.Equal(-32602, BodyContent.Error!.Code); // Invalid params
        Assert.Equal("Invalid parameters", BodyContent.Error.Message);
    }

    private static JsonElement ToJsonElement<T>(T obj)
    {
        var json = JsonSerializer.Serialize(obj, A2AJsonUtilities.DefaultOptions);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static async Task<(int StatusCode, string? ContentType, TBody BodyContent)> GetJsonRpcResponseHttpDetails<TBody>(JsonRpcResponseResult responseResult)
    {
        HttpContext context = new DefaultHttpContext();
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;
        await responseResult.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, context.Response.ContentType, JsonSerializer.Deserialize<TBody>(context.Response.Body, A2AJsonUtilities.DefaultOptions)!);
    }
}

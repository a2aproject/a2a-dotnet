using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

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
            Message = new Message { MessageId = "test-message-id", Parts = new List<Part> { new TextPart { Text = "hi" } } }
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

        var details = await GetJsonRpcResponseResultDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, details.StatusCode);
        Assert.Equal("application/json", details.ContentType);

        Assert.NotNull(details.Response.Result);

        AgentTask? agentTask = JsonSerializer.Deserialize<AgentTask>(details.Response.Result, A2AJsonUtilities.DefaultOptions);
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
        
        var details = await GetJsonRpcResponseResultDetails<JsonRpcErrorResponse>(responseResult);

        Assert.Equal(StatusCodes.Status400BadRequest, details.StatusCode);
        Assert.Equal("application/json", details.ContentType);
        
        Assert.NotNull(details.Response);
        Assert.Null(details.Response.Result);

        var error = TryGetError(details.Response);
        Assert.NotNull(error);
        Assert.Equal(-32602, error!.Code); // Invalid params
    }

    [Fact]
    public async Task ProcessRequest_Streaming_MessageStream_Works()
    {
        // Arrange
        var taskManager = new TaskManager();
        var sendParams = new MessageSendParams
        {
            Message = new Message { Parts = new List<Part> { new TextPart { Text = "hi" } } }
        };
        var req = new JsonRpcRequest
        {
            Id = "3",
            Method = A2AMethods.MessageStream,
            Params = ToJsonElement(sendParams)
        };

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequest(taskManager, req);

        // Assert
        if (result is JsonRpcResponseResult responseResult)
        {
            var details = await GetJsonRpcResponseResultDetails<JsonRpcResponse>(responseResult);
            Assert.Equal(StatusCodes.Status200OK, details.StatusCode);
            Assert.Equal("application/json", details.ContentType);
            Assert.NotNull(details.Response.Result);
        }
        else
        {
            Assert.IsType<JsonRpcStreamedResult>(result);
        }
    }

    [Fact]
    public async Task SingleResponse_TaskGet_Works()
    {
        // Arrange
        var taskManager = new TaskManager();
        var queryParams = new TaskQueryParams { Id = "test-task" };

        // Act
        var result = await A2AJsonRpcProcessor.SingleResponse(taskManager, "4", A2AMethods.TaskGet, ToJsonElement(queryParams));

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        
        var details = await GetJsonRpcResponseResultDetails<JsonRpcResponse>(responseResult);
        
        Assert.Equal(StatusCodes.Status200OK, details.StatusCode);
        Assert.Equal("application/json", details.ContentType);
        Assert.NotNull(details.Response);
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
        var details = await GetJsonRpcResponseResultDetails<JsonRpcResponse>(responseResult);
        Assert.Equal(StatusCodes.Status200OK, details.StatusCode);
        Assert.Equal("application/json", details.ContentType);
        Assert.NotNull(details.Response);
    }

    [Fact]
    public async Task SingleResponse_TaskPushNotificationConfigSet_Works()
    {
        // Arrange
        var taskManager = new TaskManager();
        var config = new TaskPushNotificationConfig { Id = "test-task", PushNotificationConfig = new PushNotificationConfig() };

        // Act
        var result = await A2AJsonRpcProcessor.SingleResponse(taskManager, "6", A2AMethods.TaskPushNotificationConfigSet, ToJsonElement(config));

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var details = await GetJsonRpcResponseResultDetails<JsonRpcResponse>(responseResult);
        Assert.Equal(StatusCodes.Status200OK, details.StatusCode);
        Assert.Equal("application/json", details.ContentType);
        Assert.NotNull(details.Response);
    }

    [Fact]
    public async Task SingleResponse_TaskPushNotificationConfigGet_Works()
    {
        // Arrange
        var taskManager = new TaskManager();
        var config = new TaskPushNotificationConfig { Id = "test-task", PushNotificationConfig = new PushNotificationConfig() };
        await taskManager.SetPushNotificationAsync(config);
        var getParams = new TaskIdParams { Id = "test-task" };

        // Act
        var result = await A2AJsonRpcProcessor.SingleResponse(taskManager, "7", A2AMethods.TaskPushNotificationConfigGet, ToJsonElement(getParams));

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var details = await GetJsonRpcResponseResultDetails<JsonRpcResponse>(responseResult);
        Assert.Equal(StatusCodes.Status200OK, details.StatusCode);
        Assert.Equal("application/json", details.ContentType);
        Assert.NotNull(details.Response);
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
        var details = await GetJsonRpcResponseResultDetails<JsonRpcResponse>(responseResult);
        Assert.Equal(StatusCodes.Status400BadRequest, details.StatusCode);
        Assert.Equal("application/json", details.ContentType);
        Assert.NotNull(details.Response);
        Assert.Null(details.Response.Result);
        var error = TryGetError(details.Response);
        Assert.NotNull(error);
        Assert.Equal(-32602, error!.Code); // Invalid params
    }

    [Fact]
    public async Task StreamResponse_UnknownMethod_ReturnsMethodNotFound()
    {
        // Arrange
        var taskManager = new TaskManager();
        var taskIdParams = new TaskIdParams { Id = "test-task" };

        // Act
        var result = await A2AJsonRpcProcessor.StreamResponse(taskManager, "11", "unknownMethod", ToJsonElement(taskIdParams));

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var details = await GetJsonRpcResponseResultDetails<JsonRpcResponse>(responseResult);
        Assert.Equal(StatusCodes.Status404NotFound, details.StatusCode);
        Assert.Equal("application/json", details.ContentType);
        Assert.NotNull(details.Response);
        Assert.Null(details.Response.Result);
        var error = TryGetError(details.Response);
        Assert.NotNull(error);
        Assert.Equal(-32601, error!.Code); // Method not found
    }

    private static JsonElement ToJsonElement<T>(T obj)
    {
        var json = JsonSerializer.Serialize(obj, A2AJsonUtilities.DefaultOptions);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static async Task<(int StatusCode, string? ContentType, TResponse Response)> GetJsonRpcResponseResultDetails<TResponse>(JsonRpcResponseResult responseResult)
    {
        HttpContext context = new DefaultHttpContext();
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;
        await responseResult.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, context.Response.ContentType, JsonSerializer.Deserialize<TResponse>(context.Response.Body, A2AJsonUtilities.DefaultOptions)!);
    }

    private static JsonRpcError? TryGetError(JsonRpcResponse response)
    {
        // Try to get the error property if present (deserialize as JsonRpcErrorResponse)
        try
        {
            var json = JsonSerializer.Serialize(response, A2AJsonUtilities.DefaultOptions);
            var errorResponse = JsonSerializer.Deserialize<JsonRpcErrorResponse>(json, A2AJsonUtilities.DefaultOptions);
            return errorResponse?.Error;
        }
        catch
        {
            return null;
        }
    }
}

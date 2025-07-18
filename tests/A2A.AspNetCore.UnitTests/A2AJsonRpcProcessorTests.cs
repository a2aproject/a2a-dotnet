using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.Text;

namespace A2A.AspNetCore.Tests;

public class A2AJsonRpcProcessorTests
{
    [Theory]
    [InlineData("\"test-id\"", true)]   // String ID - valid
    //[InlineData(42, true)]            // Number ID - valid: Uncomment when numeric IDs are supported
    [InlineData("null", true)]          // Null ID - valid
    [InlineData("true", false)]         // Boolean ID - invalid (should throw error)
    public async Task ValidateIdField_HandlesVariousIdTypes(object? idValue, bool isValid)
    {
        // Arrange
        var taskManager = new TaskManager();
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            "method": "{{A2AMethods.MessageSend}}",
            "id": {{idValue}},
            "params": {
                "message": {
                    "messageId": "test-message-id",
                    "role": "user",
                    "parts": []
                }
            }
        }
        """;

        var httpRequest = CreateHttpRequestFromJson(jsonRequest);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequest(taskManager, httpRequest);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (_, _, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        if (isValid) // Invalid ID case
        {
            Assert.NotNull(BodyContent.Result);
        }
        else // Valid ID cases
        {
            Assert.NotNull(BodyContent.Error);
            Assert.Equal(-32600, BodyContent.Error.Code); // Invalid request
            Assert.NotNull(BodyContent.Error.Message);
        }
    }

    [Theory]
    [InlineData("\"method\": \"message/send\",", null)]     // Valid method - should succeed
    [InlineData("\"method\": \"invalid/method\",", -32601)] // Invalid method - should return method not found error
    [InlineData("\"method\": \"\",", -32600)]               // Empty method - should return invalid request error
    [InlineData("", -32600)]                                // Missing method field - should return invalid request error
    public async Task ValidateMethodField_HandlesVariousMethodTypes(string methodPropertySnippet, int? expectedErrorCode)
    {
        // Arrange
        var taskManager = new TaskManager();

        // Build JSON with conditional method property inclusion
        var hasMethodProperty = !string.IsNullOrEmpty(methodPropertySnippet);
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            {{methodPropertySnippet}}
            "id": "test-id",
            "params": {
                "message": {
                    "messageId": "test-message-id",
                    "role": "user",
                    "parts": []
                }
            }
        }
        """;

        var httpRequest = CreateHttpRequestFromJson(jsonRequest);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequest(taskManager, httpRequest);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (_, _, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        if (expectedErrorCode is null)
        {
            Assert.NotNull(BodyContent.Result);
        }
        else
        {
            // For invalid methods, we expect an error
            Assert.NotNull(BodyContent.Error);
            Assert.Equal(expectedErrorCode, BodyContent.Error.Code);
            Assert.NotNull(BodyContent.Error.Message);
        }
    }

    [Theory]
    [InlineData("{\"message\":{\"messageId\":\"test\", \"role\": \"user\", \"parts\":[]}}", null)]  // Valid object params - should succeed
    [InlineData("[]", -32602)]                                                                      // Array params - should return invalid params error
    [InlineData("\"string-params\"", -32602)]                                                       // String params - should return invalid params error
    [InlineData("42", -32602)]                                                                      // Number params - should return invalid params error
    [InlineData("true", -32602)]                                                                    // Boolean params - should return invalid params error
    [InlineData("null", -32602)]                                                                    // Null params - should return invalid params error
    public async Task ValidateParamsField_HandlesVariousParamsTypes(string paramsValue, int? expectedErrorCode)
    {
        // Arrange
        var taskManager = new TaskManager();
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            "method": "{{A2AMethods.MessageSend}}",
            "id": "test-id",
            "params": {{paramsValue}}
        }
        """;

        var httpRequest = CreateHttpRequestFromJson(jsonRequest);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequest(taskManager, httpRequest);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (_, _, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        if (expectedErrorCode is null)
        {
            Assert.NotNull(BodyContent.Result);
            Assert.Null(BodyContent.Error);
        }
        else
        {
            // Invalid params cases - should return error
            Assert.Null(BodyContent.Result);
            Assert.NotNull(BodyContent.Error);
            Assert.Equal(expectedErrorCode, BodyContent.Error.Code);
            Assert.Equal("Invalid JSON-RPC request: 'params' field must be an object.", BodyContent.Error.Message);
        }
    }

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

        var httpRequest = CreateHttpRequest(req);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequest(taskManager, httpRequest);

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

        var httpRequest = CreateHttpRequest(req);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequest(taskManager, httpRequest);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);

        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode); // JSON-RPC errors return 200 with error in body
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

        var task = await taskManager.CreateTaskAsync();

        var config = new TaskPushNotificationConfig
        {
            TaskId = task.Id,
            PushNotificationConfig = new PushNotificationConfig()
            {
                Url = "https://example.com/notify",
            }
        };
        await taskManager.SetPushNotificationAsync(config);
        var getParams = new GetTaskPushNotificationConfigParams { Id = task.Id };

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

        Assert.Equal(task.Id, notificationConfig.TaskId);
        Assert.Equal("https://example.com/notify", notificationConfig.PushNotificationConfig.Url);
    }

    [Fact]
    public async Task SingleResponse_TaskPushNotificationConfigGet_WithConfigId_Works()
    {
        // Arrange
        var taskManager = new TaskManager();

        var task = await taskManager.CreateTaskAsync();

        var config = new TaskPushNotificationConfig
        {
            TaskId = task.Id,
            PushNotificationConfig = new PushNotificationConfig()
            {
                Url = "https://example.com/notify2",
                Id = "specific-config-id"
            }
        };
        await taskManager.SetPushNotificationAsync(config);
        var getParams = new GetTaskPushNotificationConfigParams
        {
            Id = task.Id,
            PushNotificationConfigId = "specific-config-id"
        };

        // Act
        var result = await A2AJsonRpcProcessor.SingleResponse(taskManager, "8", A2AMethods.TaskPushNotificationConfigGet, ToJsonElement(getParams));

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);

        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);
        Assert.NotNull(BodyContent);

        var notificationConfig = JsonSerializer.Deserialize<TaskPushNotificationConfig>(BodyContent.Result, A2AJsonUtilities.DefaultOptions);
        Assert.NotNull(notificationConfig);

        Assert.Equal(task.Id, notificationConfig.TaskId);
        Assert.Equal("https://example.com/notify2", notificationConfig.PushNotificationConfig.Url);
        Assert.Equal("specific-config-id", notificationConfig.PushNotificationConfig.Id);
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

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
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

    private static HttpRequest CreateHttpRequest(object request)
    {
        var context = new DefaultHttpContext();
        var json = JsonSerializer.Serialize(request, A2AJsonUtilities.DefaultOptions);
        return CreateHttpRequestFromJson(json);
    }

    private static HttpRequest CreateHttpRequestFromJson(string json)
    {
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentType = "application/json";
        return context.Request;
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

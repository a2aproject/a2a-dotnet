using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text;
using System.Text.Json;

namespace A2A.AspNetCore.Tests;

public class A2AJsonRpcProcessorTests
{
    [Theory]
    [InlineData("\"test-id\"", true)]   // String ID - valid
    [InlineData(42, true)]              // Number ID - valid: Uncomment when numeric IDs are supported
    [InlineData(42.1, false)]           // Fractional number ID - invalid (should throw error)
    [InlineData("null", true)]          // Null ID - valid
    [InlineData("true", false)]         // Boolean ID - invalid (should throw error)
    public async Task ValidateIdField_HandlesVariousIdTypes(object? idValue, bool isValid)
    {
        // Arrange
        var taskManager = CreateTestTaskManager();
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            "method": "{{A2AMethods.SendMessage}}",
            "id": {{idValue}},
            "params": {
                "message": {
                    "messageId": "test-message-id",
                    "role": "ROLE_USER",
                    "parts": [{"text":"hi"}]
                }
            }
        }
        """;

        var httpRequest = CreateHttpRequestFromJson(jsonRequest);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);

        if (isValid)
        {
            Assert.NotNull(BodyContent.Result);
        }
        else
        {
            Assert.NotNull(BodyContent.Error);
            Assert.Equal(-32600, BodyContent.Error.Code); // Invalid request
            Assert.NotNull(BodyContent.Error.Message);
        }
    }

    [Fact]
    public async Task EmptyPartsArrayIsNotAllowed()
    {
        // Arrange
        var taskManager = CreateTestTaskManager();
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            "method": "{{A2AMethods.SendMessage}}",
            "id": "some",
            "params": {
                "message": {
                    "messageId": "test-message-id",
                    "role": "ROLE_USER",
                    "parts": []
                }
            }
        }
        """;

        var httpRequest = CreateHttpRequestFromJson(jsonRequest);

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);
        Assert.NotNull(BodyContent.Error);
        Assert.Equal(-32602, BodyContent.Error.Code); // Invalid params
        Assert.NotNull(BodyContent.Error.Message);
    }

    [Theory]
    [InlineData("\"method\": \"SendMessage\",", null)]  // Valid method - should succeed
    [InlineData("\"method\": \"invalid/method\",", -32601)] // Invalid method - should return method not found error
    [InlineData("\"method\": \"\",", -32600)]               // Empty method - should return invalid request error
    [InlineData("", -32600)]                                // Missing method field - should return invalid request error
    public async Task ValidateMethodField_HandlesVariousMethodTypes(string methodPropertySnippet, int? expectedErrorCode)
    {
        // Arrange
        var taskManager = CreateTestTaskManager();

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
                    "role": "ROLE_USER",
                    "parts": [{"text":"hi"}]
                }
            }
        }
        """;

        var httpRequest = CreateHttpRequestFromJson(jsonRequest);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);

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
    [InlineData("{\"message\":{\"messageId\":\"test\", \"role\": \"ROLE_USER\", \"parts\": [{\"text\":\"hi\"}]}}", null)]  // Valid object params - should succeed
    [InlineData("[]", -32602)]                                                                      // Array params - should return invalid params error
    [InlineData("\"string-params\"", -32602)]                                                       // String params - should return invalid params error
    [InlineData("42", -32602)]                                                                      // Number params - should return invalid params error
    [InlineData("true", -32602)]                                                                    // Boolean params - should return invalid params error
    [InlineData("null", -32602)]                                                                    // Null params - should return invalid params error
    public async Task ValidateParamsField_HandlesVariousParamsTypes(string paramsValue, int? expectedErrorCode)
    {
        // Arrange
        var taskManager = CreateTestTaskManager();
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            "method": "{{A2AMethods.SendMessage}}",
            "id": "test-id",
            "params": {{paramsValue}}
        }
        """;

        var httpRequest = CreateHttpRequestFromJson(jsonRequest);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);

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
            Assert.NotEmpty(BodyContent.Error.Message);
        }
    }

    [Theory]
    [InlineData("{\"invalidField\": \"not_message\"}", "Invalid parameters: request body could not be deserialized as SendMessageRequest")]  // Wrong field structure
    [InlineData("{\"message\": \"not_object\"}", "Invalid parameters: request body could not be deserialized as SendMessageRequest")]        // Wrong field type
    [InlineData("{\"message\": {\"kind\": \"invalid\"}}", "Invalid parameters: request body could not be deserialized as SendMessageRequest")] // Missing required fields
    [InlineData("{\"\":\"not_a_dict\"}", "Invalid parameters: request body could not be deserialized as SendMessageRequest")] // Missing message field
    public async Task ValidateParamsContent_HandlesInvalidParamsStructure(string paramsValue, string expectedErrorPrefix)
    {
        // Arrange
        var taskManager = CreateTestTaskManager();
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            "method": "{{A2AMethods.SendMessage}}",
            "id": "test-content-validation",
            "params": {{paramsValue}}
        }
        """;

        var httpRequest = CreateHttpRequestFromJson(jsonRequest);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);

        Assert.Null(BodyContent.Result);
        Assert.NotNull(BodyContent.Error);
        Assert.Equal(-32602, BodyContent.Error.Code); // InvalidParams
        Assert.Contains(expectedErrorPrefix, BodyContent.Error.Message);
    }

    [Fact]
    public async Task ProcessRequest_SingleResponse_MessageSend_Works()
    {
        var (taskManager, _) = CreateTestTaskManagerWithStore();
        SendMessageRequest sendRequest = new()
        {
            Message = new Message { MessageId = "test-message-id", Role = Role.User, Parts = [Part.FromText("hi")] }
        };
        JsonRpcRequest req = new()
        {
            Id = "1",
            Method = A2AMethods.SendMessage,
            Params = ToJsonElement(sendRequest)
        };

        var httpRequest = CreateHttpRequest(req);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);

        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);

        Assert.NotNull(BodyContent.Result);
        var sendMessageResponse = JsonSerializer.Deserialize<SendMessageResponse>(BodyContent.Result, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(sendMessageResponse?.Task);
        var agentTask = sendMessageResponse.Task;
        Assert.Equal(TaskState.Submitted, agentTask.Status.State);
        Assert.NotEmpty(agentTask.History!);
        Assert.Equal(Role.User, agentTask.History[0].Role);
        Assert.Equal("hi", agentTask.History[0].Parts[0].Text);
        Assert.Equal("test-message-id", agentTask.History[0].MessageId);
    }

    [Fact]
    public async Task ProcessRequest_SingleResponse_InvalidParams_ReturnsError()
    {
        // Arrange
        var taskManager = CreateTestTaskManager();
        var req = new JsonRpcRequest
        {
            Id = "2",
            Method = A2AMethods.SendMessage,
            Params = null
        };

        var httpRequest = CreateHttpRequest(req);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

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
        var (taskManager, store) = CreateTestTaskManagerWithStore();
        var task = await store.SetTaskAsync(new AgentTask
        {
            Id = Guid.NewGuid().ToString(),
            ContextId = Guid.NewGuid().ToString(),
            Status = new TaskStatus { State = TaskState.Submitted }
        });

        var getTaskRequest = new GetTaskRequest { Id = task.Id };

        // Act
        var result = await A2AJsonRpcProcessor.SingleResponseAsync(taskManager, "4", A2AMethods.GetTask, ToJsonElement(getTaskRequest), CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);

        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);
        Assert.NotNull(BodyContent);

        var agentTask = JsonSerializer.Deserialize<AgentTask>(BodyContent.Result, A2AJsonUtilities.DefaultOptions);
        Assert.NotNull(agentTask);
        Assert.Equal(TaskState.Submitted, agentTask.Status.State);
    }

    [Fact]
    public async Task SingleResponse_TaskGet_NegativeHistoryLength_ReturnsFullHistory()
    {
        // Arrange - In v1, negative history length is treated as "return all history"
        var (taskManager, store) = CreateTestTaskManagerWithStore();
        var task = await store.SetTaskAsync(new AgentTask
        {
            Id = Guid.NewGuid().ToString(),
            ContextId = Guid.NewGuid().ToString(),
            Status = new TaskStatus { State = TaskState.Submitted },
            History = [new Message { MessageId = "msg1", Role = Role.User, Parts = [Part.FromText("hello")] }]
        });
        GetTaskRequest getTaskRequest = new() { Id = task.Id, HistoryLength = -1 };

        // Act
        var result = await A2AJsonRpcProcessor.SingleResponseAsync(taskManager, "4", A2AMethods.GetTask, ToJsonElement(getTaskRequest), CancellationToken.None);

        // Assert - should succeed with full history
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (StatusCode, _, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);
        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        var agentTask = JsonSerializer.Deserialize<AgentTask>(BodyContent.Result, A2AJsonUtilities.DefaultOptions);
        Assert.NotNull(agentTask);
        Assert.NotEmpty(agentTask.History!);
    }

    [Fact]
    public async Task SingleResponse_TaskCancel_Works()
    {
        // Arrange
        var (taskManager, store) = CreateTestTaskManagerWithStore();
        var newTask = await store.SetTaskAsync(new AgentTask
        {
            Id = Guid.NewGuid().ToString(),
            ContextId = Guid.NewGuid().ToString(),
            Status = new TaskStatus { State = TaskState.Submitted }
        });
        var cancelRequest = new CancelTaskRequest { Id = newTask.Id };

        // Act
        var result = await A2AJsonRpcProcessor.SingleResponseAsync(taskManager, "5", A2AMethods.CancelTask, ToJsonElement(cancelRequest), CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);

        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);
        Assert.NotNull(BodyContent);

        var agentTask = JsonSerializer.Deserialize<AgentTask>(BodyContent.Result, A2AJsonUtilities.DefaultOptions);
        Assert.NotNull(agentTask);
        Assert.Equal(TaskState.Canceled, agentTask.Status.State);
    }

    [Fact]
    public async Task StreamResponse_SendStreamingMessage_InvalidParams_ReturnsError()
    {
        // Arrange
        var taskManager = CreateTestTaskManager();

        // Act
        var result = A2AJsonRpcProcessor.StreamResponse(taskManager, "10", A2AMethods.SendStreamingMessage, null, CancellationToken.None);

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

    /// <summary>Creates a test TaskManager with in-memory store and default callbacks.</summary>
    private static IA2ARequestHandler CreateTestTaskManager()
    {
        return CreateTestTaskManagerWithStore().taskManager;
    }

    /// <summary>Creates a test TaskManager with store exposed for pre-populating data.</summary>
    private static (IA2ARequestHandler taskManager, InMemoryTaskStore store) CreateTestTaskManagerWithStore()
    {
        var store = new InMemoryTaskStore();
        var handler = new TestAgentHandler(store);
        var taskManager = new A2AServer(handler, store, NullLogger<A2AServer>.Instance);
        return (taskManager, store);
    }

    private sealed class TestAgentHandler(InMemoryTaskStore store) : IAgentHandler
    {
        public async Task ExecuteAsync(AgentContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            var task = new AgentTask
            {
                Id = context.TaskId,
                ContextId = context.ContextId,
                Status = new TaskStatus { State = TaskState.Submitted },
                History = [context.Message],
            };
            await eventQueue.EnqueueTaskAsync(task, cancellationToken);
            eventQueue.Complete();
        }

        public async Task CancelAsync(AgentContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            var task = await store.GetTaskAsync(context.TaskId, cancellationToken)
                ?? throw new A2AException("Task not found.", A2AErrorCode.TaskNotFound);
            task.Status = new TaskStatus { State = TaskState.Canceled };
            await store.SetTaskAsync(task, cancellationToken);
            eventQueue.Complete();
        }
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

    [Fact]
    public async Task ProcessRequestAsync_ListTasks_ReturnsEmptyResult()
    {
        // Arrange
        var taskManager = CreateTestTaskManager();
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            "method": "{{A2AMethods.ListTasks}}",
            "id": "list-1",
            "params": {}
        }
        """;

        var httpRequest = CreateHttpRequestFromJson(jsonRequest);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);
        Assert.NotNull(BodyContent.Result);
        Assert.Null(BodyContent.Error);

        var listResponse = JsonSerializer.Deserialize<ListTasksResponse>(BodyContent.Result, A2AJsonUtilities.DefaultOptions);
        Assert.NotNull(listResponse);
        Assert.Empty(listResponse.Tasks);
    }

    [Fact]
    public async Task ProcessRequestAsync_ListTasks_InvalidPageSize_ReturnsError()
    {
        // Arrange
        var taskManager = CreateTestTaskManager();
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            "method": "{{A2AMethods.ListTasks}}",
            "id": "list-2",
            "params": { "pageSize": 0 }
        }
        """;

        var httpRequest = CreateHttpRequestFromJson(jsonRequest);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);
        Assert.Null(BodyContent.Result);
        Assert.NotNull(BodyContent.Error);
        Assert.Equal((int)A2AErrorCode.InvalidParams, BodyContent.Error.Code);
        Assert.Contains("pageSize", BodyContent.Error.Message);
    }

    [Fact]
    public async Task ProcessRequestAsync_ListTasks_NegativeHistoryLength_ReturnsError()
    {
        // Arrange
        var taskManager = CreateTestTaskManager();
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            "method": "{{A2AMethods.ListTasks}}",
            "id": "list-3",
            "params": { "historyLength": -1 }
        }
        """;

        var httpRequest = CreateHttpRequestFromJson(jsonRequest);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);
        Assert.Null(BodyContent.Result);
        Assert.NotNull(BodyContent.Error);
        Assert.Equal((int)A2AErrorCode.InvalidParams, BodyContent.Error.Code);
        Assert.Contains("historyLength", BodyContent.Error.Message);
    }

    [Fact]
    public async Task ProcessRequestAsync_PushNotificationMethod_ReturnsNotSupported()
    {
        // Arrange
        var taskManager = CreateTestTaskManager();
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            "method": "{{A2AMethods.CreateTaskPushNotificationConfig}}",
            "id": "pn-1",
            "params": { "taskId": "some-task", "pushNotificationConfig": { "url": "https://example.com/callback" } }
        }
        """;

        var httpRequest = CreateHttpRequestFromJson(jsonRequest);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);
        Assert.Null(BodyContent.Result);
        Assert.NotNull(BodyContent.Error);
        Assert.Equal((int)A2AErrorCode.PushNotificationNotSupported, BodyContent.Error.Code);
    }

    [Fact]
    public async Task ProcessRequestAsync_GetExtendedAgentCard_ReturnsNotConfigured()
    {
        // Arrange
        var taskManager = CreateTestTaskManager();
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            "method": "{{A2AMethods.GetExtendedAgentCard}}",
            "id": "card-1",
            "params": {}
        }
        """;

        var httpRequest = CreateHttpRequestFromJson(jsonRequest);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);
        Assert.Null(BodyContent.Result);
        Assert.NotNull(BodyContent.Error);
        Assert.Equal((int)A2AErrorCode.ExtendedAgentCardNotConfigured, BodyContent.Error.Code);
    }

    [Fact]
    public async Task ProcessRequestAsync_VersionNegotiation_EmptyHeader_Succeeds()
    {
        // Arrange - no A2A-Version header set
        var taskManager = CreateTestTaskManager();
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            "method": "{{A2AMethods.SendMessage}}",
            "id": "ver-1",
            "params": {
                "message": {
                    "messageId": "test-msg",
                    "role": "ROLE_USER",
                    "parts": [{"text":"hello"}]
                }
            }
        }
        """;

        var httpRequest = CreateHttpRequestFromJson(jsonRequest);

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (StatusCode, _, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.NotNull(BodyContent.Result);
        Assert.Null(BodyContent.Error);
    }

    [Fact]
    public async Task ProcessRequestAsync_VersionNegotiation_V10_Succeeds()
    {
        // Arrange
        var taskManager = CreateTestTaskManager();
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            "method": "{{A2AMethods.SendMessage}}",
            "id": "ver-2",
            "params": {
                "message": {
                    "messageId": "test-msg",
                    "role": "ROLE_USER",
                    "parts": [{"text":"hello"}]
                }
            }
        }
        """;

        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(jsonRequest);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentType = "application/json";
        context.Request.Headers["A2A-Version"] = "1.0";
        var httpRequest = context.Request;

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (StatusCode, _, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.NotNull(BodyContent.Result);
        Assert.Null(BodyContent.Error);
    }

    [Fact]
    public async Task ProcessRequestAsync_VersionNegotiation_V03_Succeeds()
    {
        // Arrange
        var taskManager = CreateTestTaskManager();
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            "method": "{{A2AMethods.SendMessage}}",
            "id": "ver-3",
            "params": {
                "message": {
                    "messageId": "test-msg",
                    "role": "ROLE_USER",
                    "parts": [{"text":"hello"}]
                }
            }
        }
        """;

        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(jsonRequest);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentType = "application/json";
        context.Request.Headers["A2A-Version"] = "0.3";
        var httpRequest = context.Request;

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (StatusCode, _, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.NotNull(BodyContent.Result);
        Assert.Null(BodyContent.Error);
    }

    [Fact]
    public async Task ProcessRequestAsync_VersionNegotiation_Unsupported_ReturnsError()
    {
        // Arrange
        var taskManager = CreateTestTaskManager();
        var jsonRequest = $$"""
        {
            "jsonrpc": "2.0",
            "method": "{{A2AMethods.SendMessage}}",
            "id": "ver-4",
            "params": {
                "message": {
                    "messageId": "test-msg",
                    "role": "ROLE_USER",
                    "parts": [{"text":"hello"}]
                }
            }
        }
        """;

        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(jsonRequest);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentType = "application/json";
        context.Request.Headers["A2A-Version"] = "2.0";
        var httpRequest = context.Request;

        // Act
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, httpRequest, CancellationToken.None);

        // Assert
        var responseResult = Assert.IsType<JsonRpcResponseResult>(result);
        var (StatusCode, ContentType, BodyContent) = await GetJsonRpcResponseHttpDetails<JsonRpcResponse>(responseResult);

        Assert.Equal(StatusCodes.Status200OK, StatusCode);
        Assert.Equal("application/json", ContentType);
        Assert.Null(BodyContent.Result);
        Assert.NotNull(BodyContent.Error);
        Assert.Equal((int)A2AErrorCode.VersionNotSupported, BodyContent.Error.Code);
        Assert.Contains("2.0", BodyContent.Error.Message);
    }

    private static async Task<(int StatusCode, string? ContentType, TBody BodyContent)> GetJsonRpcResponseHttpDetails<TBody>(JsonRpcResponseResult responseResult)
    {
        HttpContext context = new DefaultHttpContext();
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;
        await responseResult.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var bodyContent = await JsonSerializer.DeserializeAsync<TBody>(context.Response.Body, A2AJsonUtilities.DefaultOptions);
        return (context.Response.StatusCode, context.Response.ContentType, bodyContent!);
    }
}

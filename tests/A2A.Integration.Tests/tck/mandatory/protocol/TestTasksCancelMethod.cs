using A2A.Integration.Tests.Tck.Utils;

using Microsoft.AspNetCore.Mvc.Testing;

using System.Text;
using System.Text.Json;
namespace A2A.Integration.Tests.Tck.Mandatory.Protocol;
/// <summary>
/// EXACT implementation of test_tasks_cancel_method.py from upstream TCK.
/// Tests the tasks/cancel JSON-RPC method according to A2A v0.3.0 specification.
/// </summary>
public class TestTasksCancelMethod
{
    private readonly HttpClient _client;
    public TestTasksCancelMethod()
    {
        var _factory = TransportHelpers.CreateTestApplication(
            configureTaskManager: taskManager =>
            {
                // Configure task creation for cancel tests
                taskManager.OnTaskCreated = (task, cancellationToken) =>
                {
                    return Task.CompletedTask;
                };
            });

        _client = _factory.CreateClient();

        var targetUri = new UriBuilder(_client.BaseAddress!);
        targetUri.Path = "/speccompliance";
        _client.BaseAddress = targetUri.Uri;
    }

    /// <summary>
    /// Helper method to create a task and return its ID.
    /// Matches the created_task_id fixture from upstream TCK.
    /// </summary>
    private async Task<string> CreateTaskAsync()
    {
        var testMessageId = TransportHelpers.GenerateTestMessageId("cancel-test");
        var messageSendParamsJson = $@"{{
            ""message"": {{
                ""kind"": ""message"",
                ""messageId"": ""{testMessageId}"",
                ""role"": ""user"",
                ""parts"": [
                    {{
                        ""kind"": ""text"",
                        ""text"": ""Task for cancel test""
                    }}
                ]
            }}
        }}";
        var response = await TransportHelpers.TransportSendMessage(_client, messageSendParamsJson);
        Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(response),
            $"Task creation failed: {response.RootElement}");
        var taskId = TransportHelpers.ExtractTaskIdFromResponse(response);
        Assert.NotNull(taskId);
        return taskId;
    }
    /// <summary>
    /// MANDATORY: A2A v0.3.0 §7.4 - Task Cancellation
    ///
    /// The A2A v0.3.0 specification requires all implementations to support
    /// tasks/cancel for canceling active tasks.
    /// This test works across all transport types (JSON-RPC, gRPC, REST).
    ///
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    ///
    /// Specification Reference: A2A v0.3.0 §7.4 - Task Cancellation
    /// </summary>
    [Fact]
    public async Task TestTasksCancelValidAsync()
    {
        // Arrange - Create a task first
        var taskId = await CreateTaskAsync();
        // Act - Use transport-agnostic task cancellation
        var response = await TransportHelpers.TransportCancelTask(_client, taskId);
        // Assert
        Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(response),
            $"Task cancellation failed: {response.RootElement}");
        // Extract result from transport response
        var result = response.RootElement.GetProperty("result");
        // Validate cancellation response according to A2A v0.3.0 specification
        Assert.Equal(taskId, result.GetProperty("id").GetString());
        // Check that task status indicates cancellation
        var status = result.GetProperty("status");
        if (status.ValueKind == JsonValueKind.Object)
        {
            var state = status.GetProperty("state").GetString();
            Assert.Equal("canceled", state);
        }
        else
        {
            // Handle case where status might be a string
            Assert.Equal("canceled", status.GetString());
        }
    }
    /// <summary>
    /// MANDATORY: A2A v0.3.0 §7.4 - Task Not Found Error Handling
    ///
    /// The A2A v0.3.0 specification requires proper error handling when attempting
    /// to cancel a non-existent task. MUST return TaskNotFoundError (-32001).
    /// This test works across all transport types (JSON-RPC, gRPC, REST).
    ///
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    ///
    /// Specification Reference: A2A v0.3.0 §7.4 - Task Cancellation
    /// </summary>
    [Fact]
    public async Task TestTasksCancelNonexistentAsync()
    {
        // Arrange - Use non-existent task ID
        const string nonExistentTaskId = "nonexistent-task-id";
        // Act - Use transport-agnostic task cancellation for non-existent task
        var response = await TransportHelpers.TransportCancelTask(_client, nonExistentTaskId);
        // Assert - Should receive an error response
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(response),
            $"Expected error for non-existent task, got: {response.RootElement}");
        // Validate A2A v0.3.0 TaskNotFoundError code
        var errorCode = TransportHelpers.GetErrorCode(response);
        Assert.Equal(-32001, errorCode); // TaskNotFoundError
    }
    /// <summary>
    /// MANDATORY: A2A v0.3.0 §7.4 - Task Not Cancelable Error Handling
    ///
    /// The A2A v0.3.0 specification requires proper error handling when attempting
    /// to cancel a task that is already in a terminal state.
    /// MUST return TaskNotCancelableError (-32002).
    ///
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    ///
    /// Specification Reference: A2A v0.3.0 §8.2 - A2A-Specific Errors
    /// </summary>
    [Fact]
    public async Task TestTasksCancelAlreadyTerminalAsync()
    {
        // Arrange - Create a task and cancel it first
        var taskId = await CreateTaskAsync();
        // Cancel the task once
        var firstCancelResponse = await TransportHelpers.TransportCancelTask(_client, taskId);
        Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(firstCancelResponse),
            "First cancellation should succeed");
        // Act - Try to cancel the already canceled task
        var secondCancelResponse = await TransportHelpers.TransportCancelTask(_client, taskId);
        // Assert - Should receive TaskNotCancelable error
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(secondCancelResponse),
            $"Expected error for already canceled task, got: {secondCancelResponse.RootElement}");
        var errorCode = TransportHelpers.GetErrorCode(secondCancelResponse);
        Assert.Equal(-32002, errorCode); // TaskNotCancelableError
    }
    /// <summary>
    /// MANDATORY: A2A v0.3.0 §7.4 - Invalid Parameters Error Handling
    ///
    /// The A2A v0.3.0 specification requires proper validation of tasks/cancel parameters.
    /// Missing required task ID MUST result in InvalidParamsError (-32602).
    ///
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    ///
    /// Specification Reference: A2A v0.3.0 §8.1 - Standard JSON-RPC Errors
    /// </summary>
    [Fact]
    public async Task TestTasksCancelInvalidParamsAsync()
    {
        // Arrange - Create invalid params (missing task ID)
        var jsonRpcRequest = $@"{{
            ""jsonrpc"": ""2.0"",
            ""method"": ""{A2AMethods.TaskCancel}"",
            ""params"": {{}},
            ""id"": 1
        }}";
        var content = new StringContent(jsonRpcRequest, Encoding.UTF8, "application/json");
        // Act
        var httpResponse = await _client.PostAsync(string.Empty, content);
        var responseText = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonDocument.Parse(responseText);
        // Assert - Should receive InvalidParams error
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(response),
            $"Expected error for invalid params, got: {response.RootElement}");
        var errorCode = TransportHelpers.GetErrorCode(response);
        Assert.Equal(-32602, errorCode); // InvalidParamsError
    }
    /// <summary>
    /// EDGE CASE: A2A v0.3.0 - Empty Task ID
    ///
    /// This test validates handling of empty task ID in cancellation requests.
    /// Should return appropriate error response.
    ///
    /// Failure Impact: Poor input validation
    ///
    /// Specification Reference: A2A v0.3.0 §7.4 - Task Cancellation
    /// </summary>
    [Fact]
    public async Task TestTasksCancelEmptyTaskIdAsync()
    {
        // Arrange - Use empty task ID
        const string emptyTaskId = "";
        // Act - Use transport-agnostic task cancellation with empty ID
        var response = await TransportHelpers.TransportCancelTask(_client, emptyTaskId);
        // Assert - Should receive an error response
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(response),
            $"Expected error for empty task ID, got: {response.RootElement}");
        var errorCode = TransportHelpers.GetErrorCode(response);
        // Could be TaskNotFound (-32001) or InvalidParams (-32602)
        Assert.True(errorCode == -32001 || errorCode == -32602,
            $"Expected TaskNotFound (-32001) or InvalidParams (-32602), got {errorCode}");
    }
    /// <summary>
    /// EDGE CASE: A2A v0.3.0 - Null Task ID
    ///
    /// This test validates handling of null task ID in cancellation requests.
    /// Should return appropriate error response.
    ///
    /// Failure Impact: Poor input validation
    ///
    /// Specification Reference: A2A v0.3.0 §7.4 - Task Cancellation
    /// </summary>
    [Fact]
    public async Task TestTasksCancelNullTaskIdAsync()
    {
        // Arrange - Create params with null task ID
        var jsonRpcRequest = $@"{{
            ""jsonrpc"": ""2.0"",
            ""method"": ""{A2AMethods.TaskCancel}"",
            ""params"": {{
                ""id"": null
            }},
            ""id"": 1
        }}";
        var content = new StringContent(jsonRpcRequest, Encoding.UTF8, "application/json");
        // Act
        var httpResponse = await _client.PostAsync(string.Empty, content);
        var responseText = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonDocument.Parse(responseText);
        // Assert - Should receive an error response
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(response),
            $"Expected error for null task ID, got: {response.RootElement}");
        var errorCode = TransportHelpers.GetErrorCode(response);
        // Could be TaskNotFound (-32001) or InvalidParams (-32602)
        Assert.True(errorCode == -32001 || errorCode == -32602,
            $"Expected TaskNotFound (-32001) or InvalidParams (-32602), got {errorCode}");
    }
}

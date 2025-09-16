using A2A.Integration.Tests.tck;
using A2A.Integration.Tests.Tck.Utils;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Text;
using System.Text.Json;
namespace A2A.Integration.Tests.Tck.Mandatory.Protocol;
/// <summary>
/// EXACT implementation of test_tasks_get_method.py from upstream TCK.
/// Tests the tasks/get JSON-RPC method according to A2A v0.3.0 specification.
/// </summary>
public class TestTasksGetMethod : TckClientTest
{
    /// <summary>
    /// Helper method to create a task and return its ID.
    /// Matches the created_task_id fixture from upstream TCK.
    /// </summary>
    private async Task<string> CreateTaskAsync()
    {
        var testMessageId = TransportHelpers.GenerateTestMessageId("get-test");
        var messageSendParamsJson = $@"{{
            ""message"": {{
                ""kind"": ""message"",
                ""messageId"": ""{testMessageId}"",
                ""role"": ""user"",
                ""parts"": [
                    {{
                        ""kind"": ""text"",
                        ""text"": ""Task for get test""
                    }}
                ]
            }}
        }}";

        var response = await TransportHelpers.TransportSendMessage(this.HttpClient, messageSendParamsJson);
        Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(response),
            $"Task creation failed: {response.RootElement}");

        var taskId = TransportHelpers.ExtractTaskIdFromResponse(response);
        Assert.NotNull(taskId);
        return taskId;
    }
    /// <summary>
    /// MANDATORY: A2A v0.3.0 §7.3 - Task Retrieval
    ///
    /// The A2A v0.3.0 specification requires all implementations to support
    /// tasks/get for retrieving task state and history by ID.
    /// This test works across all transport types (JSON-RPC, gRPC, REST).
    ///
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    ///
    /// Specification Reference: A2A v0.3.0 §7.3 - Task Retrieval
    /// </summary>
    [Fact]
    public async Task TestTasksGetValidAsync()
    {
        // Arrange - Create a task first
        var taskId = await CreateTaskAsync();
        // Act - Use transport-agnostic task retrieval
        var response = await TransportHelpers.TransportGetTask(this.HttpClient, taskId);
        // Assert
        Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(response),
            $"Task retrieval failed: {response.RootElement}");
        // Extract result from transport response
        var result = response.RootElement.GetProperty("result");
        // Validate task structure according to A2A v0.3.0 specification
        Assert.Equal(taskId, result.GetProperty("id").GetString());
        Assert.True(result.TryGetProperty("status", out _), "Task response must include status field");
    }
    /// <summary>
    /// MANDATORY: A2A v0.3.0 §7.3 - historyLength Parameter
    ///
    /// The A2A v0.3.0 specification states that tasks/get MUST support the historyLength
    /// parameter to limit the number of history entries returned.
    /// This test works across all transport types (JSON-RPC, gRPC, REST).
    ///
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    ///
    /// Specification Reference: A2A v0.3.0 §7.3 - Task Retrieval
    /// </summary>
    [Fact]
    public async Task TestTasksGetWithHistoryLengthAsync()
    {
        // Arrange - Create a task first
        var taskId = await CreateTaskAsync();
        // Act - Use transport-agnostic task retrieval with history length
        var response = await TransportHelpers.TransportGetTask(this.HttpClient, taskId, historyLength: 1);
        // Assert
        Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(response),
            $"Task retrieval with history failed: {response.RootElement}");
        // Extract result from transport response
        var result = response.RootElement.GetProperty("result");
        // Validate task structure according to A2A v0.3.0 specification
        Assert.Equal(taskId, result.GetProperty("id").GetString());
        Assert.True(result.TryGetProperty("history", out _),
            "Task response must include history field when historyLength is specified");
    }
    /// <summary>
    /// MANDATORY: A2A v0.3.0 §7.3 - Task Not Found Error Handling
    ///
    /// The A2A v0.3.0 specification requires proper error handling when attempting
    /// to retrieve a non-existent task. MUST return TaskNotFoundError (-32001).
    /// This test works across all transport types (JSON-RPC, gRPC, REST).
    ///
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    ///
    /// Specification Reference: A2A v0.3.0 §7.3 - Task Retrieval
    /// </summary>
    [Fact]
    public async Task TestTasksGetNonexistentAsync()
    {
        // Arrange - Use a non-existent task ID
        var nonExistentTaskId = "nonexistent-task-id";
        // Act - Use transport-agnostic task retrieval
        var response = await TransportHelpers.TransportGetTask(this.HttpClient, nonExistentTaskId);
        // Assert - Should receive an error response
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(response),
            $"Expected error for non-existent task, got: {response.RootElement}");
        // Validate A2A v0.3.0 TaskNotFoundError code
        var errorCode = TransportHelpers.GetErrorCode(response);
        Assert.Equal(-32001, errorCode); // TaskNotFoundError
    }
    /// <summary>
    /// MANDATORY: A2A v0.3.0 §7.3 - Invalid Parameters Error Handling
    ///
    /// The A2A v0.3.0 specification requires proper validation of tasks/get parameters.
    /// Missing required task ID MUST result in InvalidParamsError (-32602).
    ///
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    ///
    /// Specification Reference: A2A v0.3.0 §8.1 - Standard JSON-RPC Errors
    /// </summary>
    [Fact]
    public async Task TestTasksGetInvalidParamsAsync()
    {
        // Arrange - Create invalid params (missing task ID)
        var jsonRpcRequest = $@"{{
            ""jsonrpc"": ""2.0"",
            ""method"": ""{A2AMethods.TaskGet}"",
            ""params"": {{}},
            ""id"": 1
        }}";
        var content = new StringContent(jsonRpcRequest, Encoding.UTF8, "application/json");
        // Act
        var httpResponse = await this.HttpClient.PostAsync(string.Empty, content);
        var responseText = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonDocument.Parse(responseText);
        // Assert - Should receive InvalidParams error
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(response),
            $"Expected error for invalid params, got: {response.RootElement}");
        var errorCode = TransportHelpers.GetErrorCode(response);
        Assert.Equal(-32602, errorCode); // InvalidParamsError
    }
    /// <summary>
    /// OPTIONAL: A2A v0.3.0 - Multiple historyLength Values
    ///
    /// This test validates that different historyLength values work correctly.
    /// This helps ensure robust implementation of the historyLength parameter.
    ///
    /// Failure Impact: Limited - affects history retrieval functionality
    ///
    /// Specification Reference: A2A v0.3.0 §7.3 - Task Retrieval
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task TestTasksGetVariousHistoryLengthsAsync(int historyLength)
    {
        // Arrange - Create a task first
        var taskId = await CreateTaskAsync();
        // Act - Use transport-agnostic task retrieval with various history lengths
        var response = await TransportHelpers.TransportGetTask(this.HttpClient, taskId, historyLength: historyLength);
        // Assert - Should succeed regardless of history length value
        Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(response),
            $"Task retrieval with historyLength={historyLength} failed: {response.RootElement}");
        var result = response.RootElement.GetProperty("result");
        Assert.Equal(taskId, result.GetProperty("id").GetString());
        // If historyLength > 0, should include history field
        if (historyLength > 0)
        {
            Assert.True(result.TryGetProperty("history", out var historyProperty),
                $"Expected history field with historyLength={historyLength}");
            // History should be an array
            Assert.Equal(JsonValueKind.Array, historyProperty.ValueKind);
        }
    }
}

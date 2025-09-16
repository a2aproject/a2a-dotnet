using A2A.Integration.Tests.Tck.Utils;

using Microsoft.AspNetCore.Mvc.Testing;
namespace A2A.Integration.Tests.Tck.Mandatory.Protocol;
/// <summary>
/// EXACT implementation of test_message_send_method.py from upstream TCK.
/// Tests the message/send JSON-RPC method according to A2A v0.3.0 specification.
/// </summary>
public class TestMessageSendMethod
{
    private readonly HttpClient _client;
    private static readonly string[] ValidTaskStates = ["submitted", "working", "input-required", "completed"];

    public TestMessageSendMethod()
    {
        var _factory = TransportHelpers.CreateTestApplication(
            configureTaskManager: taskManager =>
            {
                // Configure default message handler for tests
                taskManager.OnMessageReceived = (messageSendParams, cancellationToken) =>
                {
                    return Task.FromResult<A2AResponse>(new AgentMessage
                    {
                        Role = MessageRole.Agent,
                        Parts = [new TextPart { Text = "Hello from TCK!" }],
                        MessageId = Guid.NewGuid().ToString()
                    });
                };
            });

        _client = _factory.CreateClient();

        var targetUri = new UriBuilder(_client.BaseAddress!);
        targetUri.Path = "/speccompliance";
        _client.BaseAddress = targetUri.Uri;
    }
    /// <summary>
    /// MANDATORY: A2A v0.3.0 §7.1 - Core Message Protocol
    ///
    /// The A2A v0.3.0 specification requires all implementations to support
    /// message/send with text content as the fundamental communication method.
    /// This test works across all transport types (JSON-RPC, gRPC, REST).
    ///
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    ///
    /// Specification Reference: A2A v0.3.0 §7.1 - Core Message Protocol
    /// </summary>
    [Fact]
    public async Task TestMessageSendValidTextAsync()
    {
        // Arrange - Minimal valid params for message/send (TextPart) - exactly like upstream TCK
        var testMessageId = TransportHelpers.GenerateTestMessageId("text");
        var messageSendParamsJson = $@"{{
            ""message"": {{
                ""kind"": ""message"",
                ""messageId"": ""{testMessageId}"",
                ""role"": ""user"",
                ""parts"": [
                    {{
                        ""kind"": ""text"",
                        ""text"": ""Hello from TCK!""
                    }}
                ]
            }}
        }}";
        // Act - Use transport-agnostic message sending
        var response = await TransportHelpers.TransportSendMessage(_client, messageSendParamsJson);
        // Assert - Validate response using transport-agnostic validation
        Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(response),
            $"Message send failed: {response.RootElement}");
        // Extract result from transport response
        var result = response.RootElement.GetProperty("result");
        // According to A2A v0.3.0 spec, message/send can return either Task or Message
        if (result.TryGetProperty("status", out _))
        {
            // This is a Task object
            var state = result.GetProperty("status").GetProperty("state").GetString();
            Assert.Contains(state, ValidTaskStates);
        }
        else
        {
            // This is a Message object - verify it has the expected structure
            Assert.Equal("message", result.GetProperty("kind").GetString());
            Assert.Equal("agent", result.GetProperty("role").GetString());
            Assert.True(result.TryGetProperty("parts", out _));
        }
    }
    /// <summary>
    /// MANDATORY: A2A v0.3.0 §7.1 - Parameter Validation
    ///
    /// The A2A v0.3.0 specification requires proper validation of message/send parameters.
    /// Missing required fields MUST result in InvalidParamsError (-32602).
    /// This test works across all transport types.
    ///
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    ///
    /// Specification Reference: A2A v0.3.0 §8.1 - Standard JSON-RPC Errors
    /// </summary>
    [Fact]
    public async Task TestMessageSendInvalidParamsAsync()
    {
        // Arrange - Invalid params (missing required fields)
        const string invalidParamsJson = @"{
            ""message"": {
                ""kind"": ""message""
            }
        }";

        // Act - Use transport-agnostic message sending (should fail)
        var response = await TransportHelpers.TransportSendMessage(_client, invalidParamsJson);

        // Assert - Check for proper error response across transports
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(response) ||
                   !TransportHelpers.IsJsonRpcSuccessResponse(response),
                   "Invalid parameters should result in error response");

        // For JSON-RPC, verify specific error code
        var errorCode = TransportHelpers.GetErrorCode(response);
        Assert.NotNull(errorCode);
        Assert.Equal(-32602, errorCode.Value); // InvalidParamsError
    }

    /// <summary>
    /// MANDATORY: A2A v0.3.0 §7.1 - Task Continuation
    ///
    /// The A2A v0.3.0 specification requires support for continuing existing tasks
    /// via message/send with taskId parameter. This test works across all transport types.
    ///
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    ///
    /// Specification Reference: A2A v0.3.0 §7.1 - Core Message Protocol
    /// </summary>
    [Fact]
    public async Task TestMessageSendContinueTaskAsync()
    {
        // Arrange - First, create a task using transport-agnostic sending
        var testMessageId1 = TransportHelpers.GenerateTestMessageId("initial");
        var firstParamsJson = $@"{{
            ""message"": {{
                ""kind"": ""message"",
                ""messageId"": ""{testMessageId1}"",
                ""role"": ""user"",
                ""parts"": [
                    {{
                        ""kind"": ""text"",
                        ""text"": ""Initial message""
                    }}
                ]
            }}
        }}";

        var firstResponse = await TransportHelpers.TransportSendMessage(_client, firstParamsJson);
        Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(firstResponse),
            $"Initial message send failed: {firstResponse.RootElement}");

        // Extract task ID from transport response
        var taskId = TransportHelpers.ExtractTaskIdFromResponse(firstResponse);
        Assert.NotNull(taskId);

        // Now, send a follow-up message to continue the task
        var testMessageId2 = TransportHelpers.GenerateTestMessageId("continuation");
        var continuationParamsJson = $@"{{
            ""message"": {{
                ""kind"": ""message"",
                ""taskId"": ""{taskId}"",
                ""messageId"": ""{testMessageId2}"",
                ""role"": ""user"",
                ""parts"": [
                    {{
                        ""kind"": ""text"",
                        ""text"": ""Follow-up message for the existing task""
                    }}
                ]
            }}
        }}";

        // Act
        var secondResponse = await TransportHelpers.TransportSendMessage(_client, continuationParamsJson);

        // Assert
        Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(secondResponse),
            $"Task continuation failed: {secondResponse.RootElement}");

        // Extract result from transport response
        var result = secondResponse.RootElement.GetProperty("result");

        // The result can be either a Task or Message object
        if (result.TryGetProperty("status", out _))
        {
            // This is a Task object
            var returnedTaskId = result.GetProperty("id").GetString();
            Assert.Equal(taskId, returnedTaskId);
            var state = result.GetProperty("status").GetProperty("state").GetString();
            Assert.Contains(state, ValidTaskStates);
        }
        else
        {
            // This is a Message object - verify it has the expected structure
            Assert.Equal("message", result.GetProperty("kind").GetString());
            Assert.Equal("agent", result.GetProperty("role").GetString());
            Assert.True(result.TryGetProperty("parts", out _));
        }
    }
    /// <summary>
    /// MANDATORY: A2A v0.3.0 - Task Not Found Error (Non-existent Task)
    ///
    /// The A2A v0.3.0 specification requires proper error handling when attempting
    /// to continue a non-existent task. MUST return TaskNotFoundError (-32001).
    ///
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    ///
    /// Specification Reference: A2A v0.3.0 §8.2 - A2A-Specific Errors
    /// </summary>
    [Fact]
    public async Task TestMessageSendNonExistentTaskAsync()
    {
        // Arrange - Message with non-existent task ID
        var testMessageId = TransportHelpers.GenerateTestMessageId("nonexistent");
        var nonExistentTaskParamsJson = $@"{{
            ""message"": {{
                ""kind"": ""message"",
                ""taskId"": ""non-existent-task-id"",
                ""messageId"": ""{testMessageId}"",
                ""role"": ""user"",
                ""parts"": [
                    {{
                        ""kind"": ""text"",
                        ""text"": ""Message for non-existent task""
                    }}
                ]
            }}
        }}";

        // Act
        var response = await TransportHelpers.TransportSendMessage(_client, nonExistentTaskParamsJson);

        // Assert - Should receive an error response
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(response),
            $"Expected error for non-existent task, got: {response.RootElement}");

        // Validate A2A v0.3.0 TaskNotFoundError code
        var errorCode = TransportHelpers.GetErrorCode(response);
        Assert.Equal(-32001, errorCode); // TaskNotFoundError
    }
    /// <summary>
    /// RECOMMENDED: A2A v0.3.0 - File Part Support
    ///
    /// The A2A v0.3.0 specification recommends support for file parts in messages.
    /// This test validates that file parts are handled appropriately.
    ///
    /// Failure Impact: Limited functionality - file exchange not supported
    ///
    /// Specification Reference: A2A v0.3.0 §6.5.2 - FilePart Object
    /// </summary>
    [Fact]
    public async Task TestMessageSendFilePartAsync()
    {
        // Arrange - Message with file part
        var testMessageId = TransportHelpers.GenerateTestMessageId("file");
        var fileMessageParamsJson = $@"{{
            ""message"": {{
                ""kind"": ""message"",
                ""messageId"": ""{testMessageId}"",
                ""role"": ""user"",
                ""parts"": [
                    {{
                        ""kind"": ""text"",
                        ""text"": ""Please analyze this file.""
                    }},
                    {{
                        ""kind"": ""file"",
                        ""file"": {{
                            ""name"": ""test.txt"",
                            ""mimeType"": ""text/plain"",
                            ""uri"": ""https://example.com/test.txt""
                        }}
                    }}
                ]
            }}
        }}";

        // Act
        var response = await TransportHelpers.TransportSendMessage(_client, fileMessageParamsJson);

        // Assert - Should either succeed or return appropriate error
        if (TransportHelpers.IsJsonRpcSuccessResponse(response))
        {
            // File parts are supported
            var result = response.RootElement.GetProperty("result");
            // Verify response structure (either Task or Message)
            Assert.True(result.TryGetProperty("status", out _) ||
                       result.TryGetProperty("kind", out _));
        }
        else if (TransportHelpers.IsJsonRpcErrorResponse(response))
        {
            // File parts not supported - should return appropriate error
            var errorCode = TransportHelpers.GetErrorCode(response);
            // Common error codes for unsupported content: -32005 (ContentTypeNotSupported) or -32602 (InvalidParams)
            Assert.True(errorCode == -32005 || errorCode == -32602,
                $"Expected ContentTypeNotSupported (-32005) or InvalidParams (-32602), got {errorCode}");
        }
    }

    /// <summary>
    /// RECOMMENDED: A2A v0.3.0 - Data Part Support
    ///
    /// The A2A v0.3.0 specification recommends support for structured data parts.
    /// This test validates that data parts are handled appropriately.
    ///
    /// Failure Impact: Limited functionality - structured data exchange not supported
    ///
    /// Specification Reference: A2A v0.3.0 §6.5.3 - DataPart Object
    /// </summary>
    [Fact]
    public async Task TestMessageSendDataPartAsync()
    {
        // Arrange - Message with data part
        var testMessageId = TransportHelpers.GenerateTestMessageId("data");
        var dataMessageParamsJson = $@"{{
            ""message"": {{
                ""kind"": ""message"",
                ""messageId"": ""{testMessageId}"",
                ""role"": ""user"",
                ""parts"": [
                    {{
                        ""kind"": ""text"",
                        ""text"": ""Process this data.""
                    }},
                    {{
                        ""kind"": ""data"",
                        ""data"": {{
                            ""key"": ""value"",
                            ""number"": 123,
                            ""nested"": {{
                                ""array"": [1, 2, 3]
                            }}
                        }}
                    }}
                ]
            }}
        }}";

        // Act
        var response = await TransportHelpers.TransportSendMessage(_client, dataMessageParamsJson);

        // Assert - Should either succeed or return appropriate error
        if (TransportHelpers.IsJsonRpcSuccessResponse(response))
        {
            // Data parts are supported
            var result = response.RootElement.GetProperty("result");
            // Verify response structure (either Task or Message)
            Assert.True(result.TryGetProperty("status", out _) ||
                       result.TryGetProperty("kind", out _));
        }
        else if (TransportHelpers.IsJsonRpcErrorResponse(response))
        {
            // Data parts not supported - should return appropriate error
            var errorCode = TransportHelpers.GetErrorCode(response);
            // Acceptable error codes for unsupported features
            Assert.True(errorCode == -32005 || errorCode == -32602 || errorCode == -32603,
                $"Expected valid error code for unsupported data parts, got {errorCode}");
        }
    }
}

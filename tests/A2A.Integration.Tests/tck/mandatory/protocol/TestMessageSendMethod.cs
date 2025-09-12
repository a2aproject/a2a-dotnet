using Microsoft.AspNetCore.Mvc.Testing;
using A2A.Integration.Tests.Tck.Utils;
using System.Text.Json;

namespace A2A.Integration.Tests.Tck.Mandatory.Protocol;

/// <summary>
/// EXACT implementation of test_message_send_method.py from upstream TCK.
/// Tests the message/send JSON-RPC method according to A2A v0.3.0 specification.
/// </summary>
public class TestMessageSendMethod : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public TestMessageSendMethod()
    {
        _factory = TransportHelpers.CreateTestApplication(
            configureTaskManager: taskManager =>
            {
                // Configure default message handler for tests
                taskManager.OnMessageReceived = (messageSendParams, cancellationToken) =>
                {
                    return Task.FromResult<A2AResponse>(new AgentMessage
                    {
                        Kind = "message",
                        MessageId = Guid.NewGuid().ToString(),
                        Role = MessageRole.Agent,
                        Parts = [new TextPart { Text = "Hello from TCK!" }]
                    });
                };
            });
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// MANDATORY: A2A v0.3.0 �7.1 - Core Message Protocol
    /// 
    /// The A2A v0.3.0 specification requires all implementations to support
    /// message/send with text content as the fundamental communication method.
    /// This test works across all transport types (JSON-RPC, gRPC, REST).
    /// 
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    /// 
    /// Specification Reference: A2A v0.3.0 �7.1 - Core Message Protocol
    /// </summary>
    [Fact]
    public async Task TestMessageSendValidText()
    {
        // Arrange - Minimal valid params for message/send (TextPart)
        var validTextMessageParams = new
        {
            message = new
            {
                kind = "message",
                messageId = TransportHelpers.GenerateTestMessageId("text"),
                role = "user",
                parts = new[]
                {
                    new { kind = "text", text = "Hello from TCK!" }
                }
            }
        };

        // Act - Use transport-agnostic message sending
        var response = await TransportHelpers.TransportSendMessage(_client, validTextMessageParams);

        // Assert - Validate response using transport-agnostic validation
        Xunit.Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(response), 
            $"Message send failed: {response.RootElement}");

        // Extract result from transport response
        var result = response.RootElement.GetProperty("result");

        // According to A2A v0.3.0 spec, message/send can return either Task or Message
        if (result.TryGetProperty("status", out _))
        {
            // This is a Task object
            var state = result.GetProperty("status").GetProperty("state").GetString();
            Xunit.Assert.Contains(state, new[] { "submitted", "working", "input-required", "completed" });
        }
        else
        {
            // This is a Message object - verify it has the expected structure
            Xunit.Assert.Equal("message", result.GetProperty("kind").GetString());
            Xunit.Assert.Equal("agent", result.GetProperty("role").GetString());
            Xunit.Assert.True(result.TryGetProperty("parts", out _));
        }
    }

    /// <summary>
    /// MANDATORY: A2A v0.3.0 �7.1 - Parameter Validation
    /// 
    /// The A2A v0.3.0 specification requires proper validation of message/send parameters.
    /// Missing required fields MUST result in InvalidParamsError (-32602).
    /// This test works across all transport types.
    /// 
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    /// 
    /// Specification Reference: A2A v0.3.0 �8.1 - Standard JSON-RPC Errors
    /// </summary>
    [Fact]
    public async Task TestMessageSendInvalidParams()
    {
        // Arrange - Invalid params (missing required fields)
        var invalidParams = new
        {
            message = new
            {
                kind = "message"
                // missing required fields (messageId, role, parts)
            }
        };

        // Act - Use transport-agnostic message sending (should fail)
        var response = await TransportHelpers.TransportSendMessage(_client, invalidParams);

        // Assert - Check for proper error response across transports
        Xunit.Assert.True(TransportHelpers.IsJsonRpcErrorResponse(response) || 
                   !TransportHelpers.IsJsonRpcSuccessResponse(response), 
                   "Invalid parameters should result in error response");

        // For JSON-RPC, verify specific error code
        var errorCode = TransportHelpers.GetErrorCode(response);
        if (errorCode.HasValue)
        {
            Xunit.Assert.Equal(-32602, errorCode.Value); // InvalidParamsError
        }
    }

    /// <summary>
    /// MANDATORY: A2A v0.3.0 �7.1 - Task Continuation
    /// 
    /// The A2A v0.3.0 specification requires support for continuing existing tasks
    /// via message/send with taskId parameter. This test works across all transport types.
    /// 
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    /// 
    /// Specification Reference: A2A v0.3.0 �7.1 - Core Message Protocol
    /// </summary>
    [Fact]
    public async Task TestMessageSendContinueTask()
    {
        // Arrange - First, create a task using transport-agnostic sending
        var firstParams = new
        {
            message = new
            {
                kind = "message",
                messageId = TransportHelpers.GenerateTestMessageId("initial"),
                role = "user",
                parts = new[]
                {
                    new { kind = "text", text = "Initial message" }
                }
            }
        };

        var firstResponse = await TransportHelpers.TransportSendMessage(_client, firstParams);
        Xunit.Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(firstResponse), 
            $"Initial message send failed: {firstResponse.RootElement}");

        // Extract task ID from transport response
        var taskId = TransportHelpers.ExtractTaskIdFromResponse(firstResponse);
        Xunit.Assert.NotNull(taskId);

        // Now, send a follow-up message to continue the task
        var continuationParams = new
        {
            message = new
            {
                kind = "message",
                taskId = taskId,
                messageId = TransportHelpers.GenerateTestMessageId("continuation"),
                role = "user",
                parts = new[]
                {
                    new { kind = "text", text = "Follow-up message for the existing task" }
                }
            }
        };

        // Act
        var secondResponse = await TransportHelpers.TransportSendMessage(_client, continuationParams);

        // Assert
        Xunit.Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(secondResponse), 
            $"Task continuation failed: {secondResponse.RootElement}");

        // Extract result from transport response
        var result = secondResponse.RootElement.GetProperty("result");

        // The result can be either a Task or Message object
        if (result.TryGetProperty("status", out _))
        {
            // This is a Task object
            var returnedTaskId = result.GetProperty("id").GetString();
            Xunit.Assert.Equal(taskId, returnedTaskId);
            var state = result.GetProperty("status").GetProperty("state").GetString();
            Xunit.Assert.Contains(state, new[] { "submitted", "working", "input-required", "completed" });
        }
        else
        {
            // This is a Message object - verify it has the expected structure
            Xunit.Assert.Equal("message", result.GetProperty("kind").GetString());
            Xunit.Assert.Equal("agent", result.GetProperty("role").GetString());
            Xunit.Assert.True(result.TryGetProperty("parts", out _));
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
    /// Specification Reference: A2A v0.3.0 �8.2 - A2A-Specific Errors
    /// </summary>
    [Fact]
    public async Task TestMessageSendNonExistentTask()
    {
        // Arrange - Message with non-existent task ID
        var nonExistentTaskParams = new
        {
            message = new
            {
                kind = "message",
                taskId = "non-existent-task-id",
                messageId = TransportHelpers.GenerateTestMessageId("nonexistent"),
                role = "user",
                parts = new[]
                {
                    new { kind = "text", text = "Message for non-existent task" }
                }
            }
        };

        // Act
        var response = await TransportHelpers.TransportSendMessage(_client, nonExistentTaskParams);

        // Assert - Should receive an error response
        Xunit.Assert.True(TransportHelpers.IsJsonRpcErrorResponse(response), 
            $"Expected error for non-existent task, got: {response.RootElement}");

        // Validate A2A v0.3.0 TaskNotFoundError code
        var errorCode = TransportHelpers.GetErrorCode(response);
        Xunit.Assert.Equal(-32001, errorCode); // TaskNotFoundError
    }

    /// <summary>
    /// RECOMMENDED: A2A v0.3.0 - File Part Support
    /// 
    /// The A2A v0.3.0 specification recommends support for file parts in messages.
    /// This test validates that file parts are handled appropriately.
    /// 
    /// Failure Impact: Limited functionality - file exchange not supported
    /// 
    /// Specification Reference: A2A v0.3.0 �6.5.2 - FilePart Object
    /// </summary>
    [Fact]
    public async Task TestMessageSendFilePart()
    {
        // Arrange - Message with file part
        var fileMessageParams = new
        {
            message = new
            {
                kind = "message",
                messageId = TransportHelpers.GenerateTestMessageId("file"),
                role = "user",
                parts = new object[]
                {
                    new { kind = "text", text = "Please analyze this file." },
                    new
                    {
                        kind = "file",
                        file = new
                        {
                            name = "test.txt",
                            mimeType = "text/plain", // RECOMMENDED per A2A Spec �6.6.2
                            url = "https://example.com/test.txt",
                            sizeInBytes = 1024
                        }
                    }
                }
            }
        };

        // Act
        var response = await TransportHelpers.TransportSendMessage(_client, fileMessageParams);

        // Assert - Should either succeed or return appropriate error
        if (TransportHelpers.IsJsonRpcSuccessResponse(response))
        {
            // File parts are supported
            var result = response.RootElement.GetProperty("result");
            // Verify response structure (either Task or Message)
            Xunit.Assert.True(result.TryGetProperty("status", out _) || 
                       result.TryGetProperty("kind", out _));
        }
        else if (TransportHelpers.IsJsonRpcErrorResponse(response))
        {
            // File parts not supported - should return appropriate error
            var errorCode = TransportHelpers.GetErrorCode(response);
            // Common error codes for unsupported content: -32005 (ContentTypeNotSupported) or -32602 (InvalidParams)
            Xunit.Assert.True(errorCode == -32005 || errorCode == -32602, 
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
    /// Specification Reference: A2A v0.3.0 �6.5.3 - DataPart Object
    /// </summary>
    [Fact]
    public async Task TestMessageSendDataPart()
    {
        // Arrange - Message with data part
        var dataMessageParams = new
        {
            message = new
            {
                kind = "message",
                messageId = TransportHelpers.GenerateTestMessageId("data"),
                role = "user",
                parts = new object[]
                {
                    new { kind = "text", text = "Process this data." },
                    new
                    {
                        kind = "data",
                        data = new
                        {
                            key = "value",
                            number = 123,
                            nested = new
                            {
                                array = new[] { 1, 2, 3 }
                            }
                        }
                    }
                }
            }
        };

        // Act
        var response = await TransportHelpers.TransportSendMessage(_client, dataMessageParams);

        // Assert - Should either succeed or return appropriate error
        if (TransportHelpers.IsJsonRpcSuccessResponse(response))
        {
            // Data parts are supported
            var result = response.RootElement.GetProperty("result");
            // Verify response structure (either Task or Message)
            Xunit.Assert.True(result.TryGetProperty("status", out _) || 
                       result.TryGetProperty("kind", out _));
        }
        else if (TransportHelpers.IsJsonRpcErrorResponse(response))
        {
            // Data parts not supported - should return appropriate error
            var errorCode = TransportHelpers.GetErrorCode(response);
            // Acceptable error codes for unsupported features
            Xunit.Assert.True(errorCode == -32005 || errorCode == -32602 || errorCode == -32603, 
                $"Expected valid error code for unsupported data parts, got {errorCode}");
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }
}

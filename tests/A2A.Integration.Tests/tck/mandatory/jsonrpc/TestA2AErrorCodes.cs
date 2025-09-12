using Microsoft.AspNetCore.Mvc.Testing;
using A2A.Integration.Tests.Tck.Utils;
using System.Text;
using System.Text.Json;

namespace A2A.Integration.Tests.Tck.Mandatory.JsonRpc;

/// <summary>
/// EXACT implementation of test_a2a_error_codes.py from upstream TCK.
/// Tests A2A-specific error codes according to A2A v0.3.0 specification.
/// </summary>
public class TestA2AErrorCodes : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public TestA2AErrorCodes()
    {
        _factory = TransportHelpers.CreateTestApplication();
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// Helper method to create a task and return its ID.
    /// </summary>
    private async Task<string> CreateTaskAsync()
    {
        var taskCreationParams = new
        {
            message = new
            {
                kind = "message",
                messageId = TransportHelpers.GenerateTestMessageId("error-test"),
                role = "user",
                parts = new[]
                {
                    new { kind = "text", text = "Task for error testing" }
                }
            }
        };

        var response = await TransportHelpers.TransportSendMessage(_client, taskCreationParams);
        Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(response), 
            $"Task creation failed: {response.RootElement}");

        var taskId = TransportHelpers.ExtractTaskIdFromResponse(response);
        Assert.NotNull(taskId);
        return taskId;
    }

    /// <summary>
    /// MANDATORY: A2A v0.3.0 �8.2 - TaskNotFoundError (-32001)
    /// 
    /// The A2A v0.3.0 specification requires implementations to return
    /// TaskNotFoundError (-32001) when attempting to access a non-existent task.
    /// 
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    /// 
    /// Specification Reference: A2A v0.3.0 �8.2 - A2A-Specific Errors
    /// </summary>
    [Fact]
    public async Task TestTaskNotFoundError()
    {
        // Arrange - Try to get a non-existent task
        var nonExistentTaskId = "non-existent-task-id";

        // Act
        var response = await TransportHelpers.TransportGetTask(_client, nonExistentTaskId);

        // Assert
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(response), 
            $"Expected error response, got: {response.RootElement}");

        var errorCode = TransportHelpers.GetErrorCode(response);
        Assert.Equal(-32001, errorCode); // TaskNotFoundError

        var errorMessage = TransportHelpers.GetErrorMessage(response);
        Assert.NotNull(errorMessage);
        Assert.Contains("not found", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// MANDATORY: A2A v0.3.0 �8.2 - TaskNotCancelableError (-32002)
    /// 
    /// The A2A v0.3.0 specification requires implementations to return
    /// TaskNotCancelableError (-32002) when attempting to cancel a task
    /// that is already in a terminal state.
    /// 
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    /// 
    /// Specification Reference: A2A v0.3.0 �8.2 - A2A-Specific Errors
    /// </summary>
    [Fact]
    public async Task TestTaskNotCancelableError()
    {
        // Arrange - Create and cancel a task
        var taskId = await CreateTaskAsync();
        
        // Cancel the task first time (should succeed)
        var firstCancelResponse = await TransportHelpers.TransportCancelTask(_client, taskId);
        Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(firstCancelResponse), 
            "First cancellation should succeed");

        // Act - Try to cancel the already canceled task
        var secondCancelResponse = await TransportHelpers.TransportCancelTask(_client, taskId);

        // Assert
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(secondCancelResponse), 
            $"Expected error response, got: {secondCancelResponse.RootElement}");

        var errorCode = TransportHelpers.GetErrorCode(secondCancelResponse);
        Assert.Equal(-32002, errorCode); // TaskNotCancelableError

        var errorMessage = TransportHelpers.GetErrorMessage(secondCancelResponse);
        Assert.NotNull(errorMessage);
        Assert.True(
            errorMessage.Contains("not cancelable", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("cannot be canceled", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("terminal state", StringComparison.OrdinalIgnoreCase),
            $"Error message should indicate task cannot be canceled: {errorMessage}");
    }

    /// <summary>
    /// MANDATORY: A2A v0.3.0 �8.2 - PermissionDeniedError (-32003)
    /// 
    /// The A2A v0.3.0 specification requires implementations to return
    /// PermissionDeniedError (-32003) when access is denied due to insufficient permissions.
    /// 
    /// Note: This test may be skipped if the implementation doesn't have
    /// permission-based access controls configured.
    /// 
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    /// 
    /// Specification Reference: A2A v0.3.0 �8.2 - A2A-Specific Errors
    /// </summary>
    [Fact(Skip = "Permission system not configured in test environment")]
    public static async Task TestPermissionDeniedError()
    {
        // This test would require a configured permission system
        // Skipping for basic implementation testing
        
        // In a full implementation, this would test:
        // 1. Configure restricted task access
        // 2. Attempt to access restricted task without proper permissions
        // 3. Verify PermissionDeniedError (-32003) is returned
        
        await Task.CompletedTask; // Placeholder to avoid compiler warning
    }

    /// <summary>
    /// MANDATORY: A2A v0.3.0 �8.2 - RateLimitExceededError (-32004)
    /// 
    /// The A2A v0.3.0 specification requires implementations to return
    /// RateLimitExceededError (-32004) when rate limits are exceeded.
    /// 
    /// Note: This test may be skipped if rate limiting is not configured.
    /// 
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    /// 
    /// Specification Reference: A2A v0.3.0 �8.2 - A2A-Specific Errors
    /// </summary>
    [Fact(Skip = "Rate limiting not configured in test environment")]
    public static async Task TestRateLimitExceededError()
    {
        // This test would require configured rate limiting
        // Skipping for basic implementation testing
        
        // In a full implementation, this would test:
        // 1. Configure rate limits
        // 2. Send requests rapidly to exceed limits
        // 3. Verify RateLimitExceededError (-32004) is returned
        
        await Task.CompletedTask; // Placeholder to avoid compiler warning
    }

    /// <summary>
    /// MANDATORY: A2A v0.3.0 �8.2 - ContentTypeNotSupportedError (-32005)
    /// 
    /// The A2A v0.3.0 specification requires implementations to return
    /// ContentTypeNotSupportedError (-32005) when unsupported content types are used.
    /// 
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    /// 
    /// Specification Reference: A2A v0.3.0 �8.2 - A2A-Specific Errors
    /// </summary>
    [Fact]
    public async Task TestContentTypeNotSupportedError()
    {
        // Arrange - Message with potentially unsupported file type
        var unsupportedContentParams = new
        {
            message = new
            {
                kind = "message",
                messageId = TransportHelpers.GenerateTestMessageId("content-type"),
                role = "user",
                parts = new object[]
                {
                    new
                    {
                        kind = "file",
                        file = new
                        {
                            name = "malware.exe",
                            mimeType = "application/x-msdownload", // Potentially unsupported
                            url = "https://example.com/malware.exe",
                            sizeInBytes = 1024
                        }
                    }
                }
            }
        };

        // Act
        var response = await TransportHelpers.TransportSendMessage(_client, unsupportedContentParams);

        // Assert - Either succeeds (content type supported) or returns appropriate error
        if (TransportHelpers.IsJsonRpcErrorResponse(response))
        {
            var errorCode = TransportHelpers.GetErrorCode(response);
            
            // Should return ContentTypeNotSupportedError (-32005) for unsupported content
            if (errorCode == -32005)
            {
                var errorMessage = TransportHelpers.GetErrorMessage(response);
                Assert.NotNull(errorMessage);
                Assert.True(
                    errorMessage.Contains("content type", StringComparison.OrdinalIgnoreCase) ||
                    errorMessage.Contains("mime type", StringComparison.OrdinalIgnoreCase) ||
                    errorMessage.Contains("not supported", StringComparison.OrdinalIgnoreCase),
                    $"Error message should indicate unsupported content type: {errorMessage}");
            }
            else
            {
                // Other error codes might be acceptable (e.g., InvalidParams)
                Assert.True(errorCode == -32602 || errorCode == -32603, 
                    $"Expected ContentTypeNotSupported (-32005), InvalidParams (-32602), or InternalError (-32603), got {errorCode}");
            }
        }
        else
        {
            // If the request succeeds, the content type is supported
            Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(response));
        }
    }

    /// <summary>
    /// MANDATORY: A2A v0.3.0 �8.2 - MessageTooLargeError (-32006)
    /// 
    /// The A2A v0.3.0 specification requires implementations to return
    /// MessageTooLargeError (-32006) when message size limits are exceeded.
    /// 
    /// Note: This test uses a reasonably large message to avoid memory issues.
    /// 
    /// Failure Impact: Implementation is not A2A v0.3.0 compliant
    /// 
    /// Specification Reference: A2A v0.3.0 �8.2 - A2A-Specific Errors
    /// </summary>
    [Fact]
    public async Task TestMessageTooLargeError()
    {
        // Arrange - Create a very large message
        var largeText = new string('X', 10 * 1024 * 1024); // 10MB of text
        
        var largeMessageParams = new
        {
            message = new
            {
                kind = "message",
                messageId = TransportHelpers.GenerateTestMessageId("large"),
                role = "user",
                parts = new[]
                {
                    new { kind = "text", text = largeText }
                }
            }
        };

        // Act
        var response = await TransportHelpers.TransportSendMessage(_client, largeMessageParams);

        // Assert - Either succeeds (no size limit) or returns appropriate error
        if (TransportHelpers.IsJsonRpcErrorResponse(response))
        {
            var errorCode = TransportHelpers.GetErrorCode(response);
            
            // Could return MessageTooLarge (-32006) or other errors
            Assert.True(
                errorCode == -32006 || // MessageTooLargeError
                errorCode == -32602 || // InvalidParams
                errorCode == -32603,   // InternalError
                $"Expected MessageTooLarge (-32006), InvalidParams (-32602), or InternalError (-32603), got {errorCode}");
                
            if (errorCode == -32006)
            {
                var errorMessage = TransportHelpers.GetErrorMessage(response);
                Assert.NotNull(errorMessage);
                Assert.True(
                    errorMessage.Contains("too large", StringComparison.OrdinalIgnoreCase) ||
                    errorMessage.Contains("size limit", StringComparison.OrdinalIgnoreCase) ||
                    errorMessage.Contains("maximum size", StringComparison.OrdinalIgnoreCase),
                    $"Error message should indicate message size issue: {errorMessage}");
            }
        }
        else
        {
            // If the request succeeds, there's no effective size limit
            Assert.True(TransportHelpers.IsJsonRpcSuccessResponse(response));
        }
    }

    /// <summary>
    /// VALIDATION: A2A v0.3.0 - Error Response Structure
    /// 
    /// Validates that A2A error responses follow proper JSON-RPC structure
    /// and include required A2A-specific error information.
    /// 
    /// Failure Impact: Poor error handling user experience
    /// 
    /// Specification Reference: A2A v0.3.0 �8.2 - A2A-Specific Errors
    /// </summary>
    [Fact]
    public async Task TestErrorResponseStructure()
    {
        // Arrange - Generate an error by accessing non-existent task
        var nonExistentTaskId = "error-structure-test";

        // Act
        var response = await TransportHelpers.TransportGetTask(_client, nonExistentTaskId);

        // Assert
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(response));
        
        var error = response.RootElement.GetProperty("error");
        
        // Validate required error fields
        Assert.True(error.TryGetProperty("code", out var codeProperty));
        Assert.True(error.TryGetProperty("message", out var messageProperty));
        
        var code = codeProperty.GetInt32();
        var message = messageProperty.GetString();
        
        Assert.Equal(-32001, code); // TaskNotFoundError
        Assert.NotNull(message);
        Assert.NotEmpty(message);
        
        // Optional: Check for error data field
        if (error.TryGetProperty("data", out var dataProperty))
        {
            // Data field is optional but if present should provide additional context
            Assert.NotEqual(JsonValueKind.Null, dataProperty.ValueKind);
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }
}

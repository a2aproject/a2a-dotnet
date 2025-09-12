using Microsoft.AspNetCore.Mvc.Testing;
using A2A.Integration.Tests.Tck.Utils;
using System.Text;
using System.Text.Json;

namespace A2A.Integration.Tests.Tck.Mandatory.JsonRpc;

/// <summary>
/// EXACT implementation of test_json_rpc_compliance.py from upstream TCK.
/// Tests JSON-RPC 2.0 protocol compliance according to the specification.
/// </summary>
public class TestJsonRpcCompliance : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public TestJsonRpcCompliance()
    {
        _factory = TransportHelpers.CreateTestApplication();
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// MANDATORY: JSON-RPC 2.0 Specification �4.2 - Parse Error
    /// 
    /// The server MUST reject syntactically invalid JSON with error code -32700.
    /// This is a hard requirement for JSON-RPC compliance.
    /// 
    /// Failure Impact: Implementation is not JSON-RPC 2.0 compliant
    /// </summary>
    [Fact]
    public async Task TestRejectsMalformedJson()
    {
        // Arrange - Malformed JSON (missing closing brace)
        var malformedJson = """{"jsonrpc": "2.0", "method": "message/send", "params": {"foo": "bar"}""";
        
        var content = new StringContent(malformedJson, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/", content);

        // Assert - Should return JSON-RPC Parse Error
        Xunit.Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode); // JSON-RPC errors use 200 OK
        
        var responseText = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseText);
        
        Xunit.Assert.True(TransportHelpers.IsJsonRpcErrorResponse(responseJson));
        var errorCode = TransportHelpers.GetErrorCode(responseJson);
        Xunit.Assert.Equal(-32700, errorCode); // ParseError
    }

    /// <summary>
    /// MANDATORY: JSON-RPC 2.0 Specification �4.1 & �4.3 - Request Structure
    /// 
    /// The server MUST have required fields (jsonrpc, method) and MUST return
    /// proper error codes for invalid requests: -32600 (Invalid Request),
    /// -32601 (Method not found), -32602 (Invalid params).
    /// 
    /// Failure Impact: Implementation is not JSON-RPC 2.0 compliant
    /// </summary>
    [Theory]
    [InlineData("""{"jsonrpc": "aaa", "method": "message/send", "params": {}}""", -32600)] // invalid jsonrpc version
    [InlineData("""{"jsonrpc": "2.0", "params": {}}""", -32600)] // missing method
    [InlineData("""{"jsonrpc": "2.0", "method": "message/ssend", "params": {}}""", -32601)] // wrong method
    [InlineData("""{"jsonrpc": "2.0", "method": "message/send", "params": {}, "id": {"bad": "type"}}""", -32600)] // invalid id type
    [InlineData("""{"jsonrpc": "2.0", "method": "message/send", "params": "not_a_dict"}""", -32602)] // invalid params type
    public async Task TestRejectsInvalidJsonRpcRequests(string invalidRequest, int expectedErrorCode)
    {
        // Arrange
        var content = new StringContent(invalidRequest, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/", content);

        // Assert
        Xunit.Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode); // JSON-RPC errors use 200 OK
        
        var responseText = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseText);
        
        Xunit.Assert.True(TransportHelpers.IsJsonRpcErrorResponse(responseJson));
        var errorCode = TransportHelpers.GetErrorCode(responseJson);
        Xunit.Assert.Equal(expectedErrorCode, errorCode);
    }

    /// <summary>
    /// MANDATORY: JSON-RPC 2.0 Specification �4.3 - Method Not Found
    /// 
    /// The server MUST reject requests with undefined method names
    /// with error code -32601 (Method not found).
    /// 
    /// Failure Impact: Implementation is not JSON-RPC 2.0 compliant
    /// </summary>
    [Fact]
    public async Task TestRejectsUnknownMethod()
    {
        // Arrange - Valid JSON-RPC request with unknown method
        var unknownMethodRequest = new
        {
            jsonrpc = "2.0",
            method = "nonexistent/method",
            @params = new { },
            id = 1
        };

        var content = new StringContent(
            JsonSerializer.Serialize(unknownMethodRequest, A2AJsonUtilities.DefaultOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/", content);

        // Assert
        Xunit.Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        
        var responseText = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseText);
        
        Xunit.Assert.True(TransportHelpers.IsJsonRpcErrorResponse(responseJson));
        var errorCode = TransportHelpers.GetErrorCode(responseJson);
        Xunit.Assert.Equal(-32601, errorCode); // MethodNotFoundError
    }

    /// <summary>
    /// MANDATORY: JSON-RPC 2.0 Specification �4.3 - Invalid Parameters
    /// 
    /// The server MUST reject requests with invalid parameters
    /// with error code -32602 (Invalid params).
    /// 
    /// Failure Impact: Implementation is not JSON-RPC 2.0 compliant
    /// </summary>
    [Fact]
    public async Task TestRejectsInvalidParams()
    {
        // Arrange - Valid method but invalid params structure
        var invalidParamsRequest = new
        {
            jsonrpc = "2.0",
            method = A2AMethods.MessageSend,
            @params = new
            {
                message = new { } // Empty message object - missing required fields
            },
            id = 1
        };

        var content = new StringContent(
            JsonSerializer.Serialize(invalidParamsRequest, A2AJsonUtilities.DefaultOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/", content);

        // Assert
        Xunit.Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        
        var responseText = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseText);
        
        Xunit.Assert.True(TransportHelpers.IsJsonRpcErrorResponse(responseJson));
        var errorCode = TransportHelpers.GetErrorCode(responseJson);
        Xunit.Assert.Equal(-32602, errorCode); // InvalidParamsError
    }

    /// <summary>
    /// MANDATORY: JSON-RPC 2.0 Specification �4.1 - Request ID Handling
    /// 
    /// The server MUST properly handle request IDs and include them in responses.
    /// 
    /// Failure Impact: Implementation is not JSON-RPC 2.0 compliant
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData("string-id")]
    [InlineData(null)]
    public async Task TestRequestIdHandling(object? requestId)
    {
        // Arrange - Valid request with different ID types
        var validRequest = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["method"] = A2AMethods.MessageSend,
            ["params"] = new
            {
                message = new
                {
                    kind = "message",
                    messageId = TransportHelpers.GenerateTestMessageId(),
                    role = "user",
                    parts = new[]
                    {
                        new { kind = "text", text = "Test message" }
                    }
                }
            }
        };

        if (requestId != null)
        {
            validRequest["id"] = requestId;
        }

        var content = new StringContent(
            JsonSerializer.Serialize(validRequest, A2AJsonUtilities.DefaultOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/", content);

        // Assert
        Xunit.Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        
        var responseText = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseText);
        
        // Response should include the same ID (or null if no ID was provided)
        if (requestId != null)
        {
            Xunit.Assert.True(responseJson.RootElement.TryGetProperty("id", out var responseIdProperty));
            
            if (requestId is int intId)
            {
                Xunit.Assert.Equal(intId, responseIdProperty.GetInt32());
            }
            else if (requestId is string stringId)
            {
                Xunit.Assert.Equal(stringId, responseIdProperty.GetString());
            }
        }
        else
        {
            // Notification (no ID) - response handling varies by implementation
            // Some implementations may not send a response for notifications
        }
    }

    /// <summary>
    /// MANDATORY: JSON-RPC 2.0 Specification �4.1 - Version Field Requirement
    /// 
    /// The server MUST require the "jsonrpc": "2.0" field in all requests.
    /// 
    /// Failure Impact: Implementation is not JSON-RPC 2.0 compliant
    /// </summary>
    [Theory]
    [InlineData("1.0")]
    [InlineData("3.0")]
    [InlineData("")]
    [InlineData("invalid")]
    public async Task TestJsonRpcVersionValidation(string version)
    {
        // Arrange - Request with invalid jsonrpc version
        var invalidVersionRequest = new
        {
            jsonrpc = version,
            method = A2AMethods.MessageSend,
            @params = new
            {
                message = new
                {
                    kind = "message",
                    messageId = TransportHelpers.GenerateTestMessageId(),
                    role = "user",
                    parts = new[]
                    {
                        new { kind = "text", text = "Test message" }
                    }
                }
            },
            id = 1
        };

        var content = new StringContent(
            JsonSerializer.Serialize(invalidVersionRequest, A2AJsonUtilities.DefaultOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/", content);

        // Assert
        Xunit.Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        
        var responseText = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseText);
        
        Xunit.Assert.True(TransportHelpers.IsJsonRpcErrorResponse(responseJson));
        var errorCode = TransportHelpers.GetErrorCode(responseJson);
        Xunit.Assert.Equal(-32600, errorCode); // InvalidRequestError
    }

    /// <summary>
    /// MANDATORY: JSON-RPC 2.0 Specification �5 - Response Structure
    /// 
    /// The server MUST return proper JSON-RPC 2.0 response structure.
    /// 
    /// Failure Impact: Implementation is not JSON-RPC 2.0 compliant
    /// </summary>
    [Fact]
    public async Task TestValidResponseStructure()
    {
        // Arrange - Valid request
        var validRequest = new
        {
            jsonrpc = "2.0",
            method = A2AMethods.MessageSend,
            @params = new
            {
                message = new
                {
                    kind = "message",
                    messageId = TransportHelpers.GenerateTestMessageId(),
                    role = "user",
                    parts = new[]
                    {
                        new { kind = "text", text = "Test message" }
                    }
                }
            },
            id = 42
        };

        var content = new StringContent(
            JsonSerializer.Serialize(validRequest, A2AJsonUtilities.DefaultOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/", content);

        // Assert
        Xunit.Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        
        var responseText = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseText);
        
        // Validate JSON-RPC 2.0 response structure
        Xunit.Assert.Equal("2.0", responseJson.RootElement.GetProperty("jsonrpc").GetString());
        Xunit.Assert.Equal(42, responseJson.RootElement.GetProperty("id").GetInt32());
        
        // Should have either "result" or "error", but not both
        var hasResult = responseJson.RootElement.TryGetProperty("result", out _);
        var hasError = responseJson.RootElement.TryGetProperty("error", out _);
        
        Xunit.Assert.True(hasResult ^ hasError, "Response should have either 'result' or 'error', but not both");
    }

    public void Dispose()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }
}

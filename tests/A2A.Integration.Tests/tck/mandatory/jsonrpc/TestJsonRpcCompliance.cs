using A2A.Integration.Tests.tck;
using A2A.Integration.Tests.Tck.Utils;

using Microsoft.AspNetCore.Mvc.Testing;

using System.Text;
namespace A2A.Integration.Tests.Tck.Mandatory.JsonRpc;
/// <summary>
/// EXACT implementation of test_json_rpc_compliance.py from upstream TCK.
/// Tests JSON-RPC 2.0 protocol compliance according to the specification.
/// </summary>
public class TestJsonRpcCompliance : TckClientTest
{
    /// <summary>
    /// MANDATORY: JSON-RPC 2.0 Specification §4.2 - Parse Error
    ///
    /// The server MUST reject syntactically invalid JSON with error code -32700.
    /// This is a hard requirement for JSON-RPC compliance.
    ///
    /// Failure Impact: Implementation is not JSON-RPC 2.0 compliant
    /// </summary>
    [Fact]
    public async Task TestRejectsMalformedJsonAsync()
    {
        // Arrange - Malformed JSON (missing closing brace)
        var malformedJson = """{"jsonrpc": "2.0", "method": "message/send", "params": {"foo": "bar"}""";
        var content = new StringContent(malformedJson, Encoding.UTF8, "application/json");
        // Act
        var response = await this.HttpClient.PostAsync(string.Empty, content);
        // Assert - Should return JSON-RPC Parse Error
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode); // JSON-RPC errors use 200 OK
        var responseText = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseText);
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(responseJson));
        var errorCode = TransportHelpers.GetErrorCode(responseJson);
        Assert.Equal(-32700, errorCode); // ParseError
    }
    /// <summary>
    /// MANDATORY: JSON-RPC 2.0 Specification §4.1 & §4.3 - Request Structure
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
    public async Task TestRejectsInvalidJsonRpcRequestsAsync(string invalidRequest, int expectedErrorCode)
    {
        // Arrange
        var content = new StringContent(invalidRequest, Encoding.UTF8, "application/json");
        // Act
        var response = await this.HttpClient.PostAsync(string.Empty, content);
        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode); // JSON-RPC errors use 200 OK
        var responseText = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseText);
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(responseJson));
        var errorCode = TransportHelpers.GetErrorCode(responseJson);
        Assert.Equal(expectedErrorCode, errorCode);
    }
    /// <summary>
    /// MANDATORY: JSON-RPC 2.0 Specification §4.3 - Method Not Found
    ///
    /// The server MUST reject requests with undefined method names
    /// with error code -32601 (Method not found).
    ///
    /// Failure Impact: Implementation is not JSON-RPC 2.0 compliant
    /// </summary>
    [Fact]
    public async Task TestRejectsUnknownMethodAsync()
    {
        // Arrange - Valid JSON-RPC request with unknown method
        var jsonRpcRequest = """
        {
            "jsonrpc": "2.0",
            "method": "nonexistent/method",
            "params": {},
            "id": 1
        }
        """;
        var content = new StringContent(jsonRpcRequest, Encoding.UTF8, "application/json");
        // Act
        var response = await this.HttpClient.PostAsync(string.Empty, content);
        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseText);
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(responseJson));
        var errorCode = TransportHelpers.GetErrorCode(responseJson);
        Assert.Equal(-32601, errorCode); // MethodNotFoundError
    }
    /// <summary>
    /// MANDATORY: JSON-RPC 2.0 Specification §4.3 - Invalid Parameters
    ///
    /// The server MUST reject requests with invalid parameters
    /// with error code -32602 (Invalid params).
    ///
    /// Failure Impact: Implementation is not JSON-RPC 2.0 compliant
    /// </summary>
    [Fact]
    public async Task TestRejectsInvalidParamsAsync()
    {
        // Arrange - Valid method but invalid params structure
        var jsonRpcRequest = $@"{{
            ""jsonrpc"": ""2.0"",
            ""method"": ""{A2AMethods.MessageSend}"",
            ""params"": {{
                ""message"": {{}}
            }},
            ""id"": 1
        }}";
        var content = new StringContent(jsonRpcRequest, Encoding.UTF8, "application/json");
        // Act
        var response = await this.HttpClient.PostAsync(string.Empty, content);
        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseText);
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(responseJson));
        var errorCode = TransportHelpers.GetErrorCode(responseJson);
        Assert.Equal(-32602, errorCode); // InvalidParamsError
    }
    /// <summary>
    /// MANDATORY: JSON-RPC 2.0 Specification §4.1 - Request ID Handling
    ///
    /// The server MUST properly handle request IDs and include them in responses.
    ///
    /// Failure Impact: Implementation is not JSON-RPC 2.0 compliant
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData("string-id")]
    [InlineData(null)]
    public async Task TestRequestIdHandlingAsync(object? requestId)
    {
        // Arrange - Valid request with different ID types
        var testMessageId = TransportHelpers.GenerateTestMessageId();
        var baseRequest = $@"{{
            ""jsonrpc"": ""2.0"",
            ""method"": ""{A2AMethods.MessageSend}"",
            ""params"": {{
                ""message"": {{
                    ""kind"": ""message"",
                    ""messageId"": ""{testMessageId}"",
                    ""role"": ""user"",
                    ""parts"": [
                        {{
                            ""kind"": ""text"",
                            ""text"": ""Test message""
                        }}
                    ]
                }}
            }}";
        var jsonRpcRequest = requestId switch
        {
            int intId => baseRequest + $@",
            ""id"": {intId}
        }}",
            string stringId => baseRequest + $@",
            ""id"": ""{stringId}""
        }}",
            null => baseRequest + "\n}",
            _ => throw new ArgumentException($"Unsupported request ID type: {requestId?.GetType()}")
        };
        var content = new StringContent(jsonRpcRequest, Encoding.UTF8, "application/json");
        // Act
        var response = await this.HttpClient.PostAsync(string.Empty, content);
        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseText);
        // Response should include the same ID (or null if no ID was provided)
        if (requestId != null)
        {
            Assert.True(responseJson.RootElement.TryGetProperty("id", out var responseIdProperty));
            if (requestId is int intId)
            {
                Assert.Equal(intId, responseIdProperty.GetInt32());
            }
            else if (requestId is string stringId)
            {
                Assert.Equal(stringId, responseIdProperty.GetString());
            }
        }
        else
        {
            // Notification (no ID) - response handling varies by implementation
            // Some implementations may not send a response for notifications
        }
    }
    /// <summary>
    /// MANDATORY: JSON-RPC 2.0 Specification §4.1 - Version Field Requirement
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
    public async Task TestJsonRpcVersionValidationAsync(string version)
    {
        // Arrange - Request with invalid jsonrpc version
        var testMessageId = TransportHelpers.GenerateTestMessageId();
        var jsonRpcRequest = $@"{{
            ""jsonrpc"": ""{version}"",
            ""method"": ""{A2AMethods.MessageSend}"",
            ""params"": {{
                ""message"": {{
                    ""kind"": ""message"",
                    ""messageId"": ""{testMessageId}"",
                    ""role"": ""user"",
                    ""parts"": [
                        {{
                            ""kind"": ""text"",
                            ""text"": ""Test message""
                        }}
                    ]
                }}
            }},
            ""id"": 1
        }}";
        var content = new StringContent(jsonRpcRequest, Encoding.UTF8, "application/json");
        // Act
        var response = await this.HttpClient.PostAsync(string.Empty, content);
        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseText);
        Assert.True(TransportHelpers.IsJsonRpcErrorResponse(responseJson));
        var errorCode = TransportHelpers.GetErrorCode(responseJson);
        Assert.Equal(-32600, errorCode); // InvalidRequestError
    }
    /// <summary>
    /// MANDATORY: JSON-RPC 2.0 Specification §5 - Response Structure
    ///
    /// The server MUST return proper JSON-RPC 2.0 response structure.
    ///
    /// Failure Impact: Implementation is not JSON-RPC 2.0 compliant
    /// </summary>
    [Fact]
    public async Task TestValidResponseStructureAsync()
    {
        // Arrange - Valid request
        var testMessageId = TransportHelpers.GenerateTestMessageId();
        var jsonRpcRequest = $@"{{
            ""jsonrpc"": ""2.0"",
            ""method"": ""{A2AMethods.MessageSend}"",
            ""params"": {{
                ""message"": {{
                    ""kind"": ""message"",
                    ""messageId"": ""{testMessageId}"",
                    ""role"": ""user"",
                    ""parts"": [
                        {{
                            ""kind"": ""text"",
                            ""text"": ""Test message""
                        }}
                    ]
                }}
            }},
            ""id"": 42
        }}";
        var content = new StringContent(jsonRpcRequest, Encoding.UTF8, "application/json");
        // Act
        var response = await this.HttpClient.PostAsync(string.Empty, content);
        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseText);
        // Validate JSON-RPC 2.0 response structure
        Assert.Equal("2.0", responseJson.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal(42, responseJson.RootElement.GetProperty("id").GetInt32());
        // Should have either "result" or "error", but not both
        var hasResult = responseJson.RootElement.TryGetProperty("result", out _);
        var hasError = responseJson.RootElement.TryGetProperty("error", out _);
        Assert.True(hasResult ^ hasError, "Response should have either 'result' or 'error', but not both");
    }
}

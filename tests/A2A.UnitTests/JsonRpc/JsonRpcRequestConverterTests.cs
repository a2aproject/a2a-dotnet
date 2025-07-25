using A2A.AspNetCore;
using System.Text.Json;

namespace A2A.UnitTests.JsonRpc;

public class JsonRpcRequestConverterTests
{
    private readonly JsonSerializerOptions _options;

    public JsonRpcRequestConverterTests()
    {
        _options = new JsonSerializerOptions();
        _options.Converters.Add(new JsonRpcRequestConverter());
    }

    #region Successful Deserialization Tests

    [Fact]
    public void Read_ValidJsonRpcRequest_WithAllFields_ReturnsRequest()
    {
        // Arrange
        var json = """
        {
            "jsonrpc": "2.0",
            "id": "test-id",
            "method": "message/send",
            "params": {
                "message": {
                    "messageId": "msg-1",
                    "role": "user",
                    "parts": []
                }
            }
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<JsonRpcRequest>(json, _options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("2.0", result.JsonRpc);
        Assert.Equal("test-id", result.Id);
        Assert.Equal("message/send", result.Method);
        Assert.True(result.Params.HasValue);
        Assert.True(result.Params.Value.TryGetProperty("message", out _));
    }

    [Fact]
    public void Read_ValidJsonRpcRequest_WithoutParams_ReturnsRequest()
    {
        // Arrange
        var json = """
        {
            "jsonrpc": "2.0",
            "id": "test-id",
            "method": "tasks/get"
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<JsonRpcRequest>(json, _options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("2.0", result.JsonRpc);
        Assert.Equal("test-id", result.Id);
        Assert.Equal("tasks/get", result.Method);
        Assert.False(result.Params.HasValue);
    }

    [Fact]
    public void Read_ValidJsonRpcRequest_WithoutId_ReturnsRequest()
    {
        // Arrange
        var json = """
        {
            "jsonrpc": "2.0",
            "method": "message/send",
            "params": {}
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<JsonRpcRequest>(json, _options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("2.0", result.JsonRpc);
        Assert.Null(result.Id);
        Assert.Equal("message/send", result.Method);
        Assert.True(result.Params.HasValue);
    }

    [Theory]
    [InlineData("\"string-id\"", "string-id")]
    [InlineData("123", "123")]
    [InlineData("null", null)]
    public void Read_ValidIdTypes_ReturnsCorrectId(string idJson, string? expectedId)
    {
        // Arrange
        var json = $$"""
        {
            "jsonrpc": "2.0",
            "id": {{idJson}},
            "method": "tasks/get"
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<JsonRpcRequest>(json, _options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedId, result.Id);
    }

    [Theory]
    [InlineData("message/send")]
    [InlineData("message/stream")]
    [InlineData("tasks/get")]
    [InlineData("tasks/cancel")]
    [InlineData("tasks/subscribe")]
    [InlineData("tasks/pushNotificationConfig/set")]
    [InlineData("tasks/pushNotificationConfig/get")]
    public void Read_ValidMethods_ReturnsCorrectMethod(string method)
    {
        // Arrange
        var json = $$"""
        {
            "jsonrpc": "2.0",
            "id": "test-id",
            "method": "{{method}}"
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<JsonRpcRequest>(json, _options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(method, result.Method);
    }

    #endregion

    #region Validation Error Tests

    [Fact]
    public void Read_MissingJsonRpcField_ThrowsA2AException()
    {
        // Arrange
        var json = """
        {
            "id": "test-id",
            "method": "tasks/get"
        }
        """;

        // Act & Assert
        var exception = Assert.Throws<A2AException>(() =>
            JsonSerializer.Deserialize<JsonRpcRequest>(json, _options));

        Assert.Equal(A2AErrorCode.InvalidRequest, exception.ErrorCode);
        Assert.Contains("missing 'jsonrpc' field", exception.Message);
    }

    [Theory]
    [InlineData("\"1.0\"")]
    [InlineData("\"3.0\"")]
    [InlineData("\"invalid\"")]
    [InlineData("null")]
    public void Read_InvalidJsonRpcVersion_ThrowsA2AException(string versionJson)
    {
        // Arrange
        var json = $$"""
        {
            "jsonrpc": {{versionJson}},
            "id": "test-id",
            "method": "tasks/get"
        }
        """;

        // Act & Assert
        var exception = Assert.Throws<A2AException>(() =>
            JsonSerializer.Deserialize<JsonRpcRequest>(json, _options));

        Assert.Equal(A2AErrorCode.InvalidRequest, exception.ErrorCode);
        Assert.Contains("'jsonrpc' field must be '2.0'", exception.Message);
    }

    [Fact]
    public void Read_MissingMethodField_ThrowsA2AException()
    {
        // Arrange
        var json = """
        {
            "jsonrpc": "2.0",
            "id": "test-id"
        }
        """;

        // Act & Assert
        var exception = Assert.Throws<A2AException>(() =>
            JsonSerializer.Deserialize<JsonRpcRequest>(json, _options));

        Assert.Equal(A2AErrorCode.InvalidRequest, exception.ErrorCode);
        Assert.Contains("missing 'method' field", exception.Message);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("null")]
    public void Read_EmptyOrNullMethod_ThrowsA2AException(string methodJson)
    {
        // Arrange
        var json = $$"""
        {
            "jsonrpc": "2.0",
            "id": "test-id",
            "method": {{methodJson}}
        }
        """;

        // Act & Assert
        var exception = Assert.Throws<A2AException>(() =>
            JsonSerializer.Deserialize<JsonRpcRequest>(json, _options));

        Assert.Equal(A2AErrorCode.InvalidRequest, exception.ErrorCode);
        Assert.Contains("missing 'method' field", exception.Message);
    }

    [Theory]
    [InlineData("\"invalid/method\"")]
    [InlineData("\"unknown\"")]
    [InlineData("\"message/invalid\"")]
    public void Read_InvalidMethod_ThrowsA2AException(string methodJson)
    {
        // Arrange
        var json = $$"""
        {
            "jsonrpc": "2.0",
            "id": "test-id",
            "method": {{methodJson}}
        }
        """;

        // Act & Assert
        var exception = Assert.Throws<A2AException>(() =>
            JsonSerializer.Deserialize<JsonRpcRequest>(json, _options));

        Assert.Equal(A2AErrorCode.MethodNotFound, exception.ErrorCode);
        Assert.Contains("not a valid A2A method", exception.Message);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("[]")]
    public void Read_InvalidIdType_ThrowsA2AException(string idJson)
    {
        // Arrange
        var json = $$"""
        {
            "jsonrpc": "2.0",
            "id": {{idJson}},
            "method": "tasks/get"
        }
        """;

        // Act & Assert
        var exception = Assert.Throws<A2AException>(() =>
            JsonSerializer.Deserialize<JsonRpcRequest>(json, _options));

        Assert.Equal(A2AErrorCode.InvalidRequest, exception.ErrorCode);
        Assert.Contains("'id' field must be a string, number, or null", exception.Message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"string\"")]
    [InlineData("123")]
    [InlineData("true")]
    public void Read_InvalidParamsType_ThrowsA2AException(string paramsJson)
    {
        // Arrange
        var json = $$"""
        {
            "jsonrpc": "2.0",
            "id": "test-id",
            "method": "tasks/get",
            "params": {{paramsJson}}
        }
        """;

        // Act & Assert
        var exception = Assert.Throws<A2AException>(() =>
            JsonSerializer.Deserialize<JsonRpcRequest>(json, _options));

        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Contains("'params' field must be an object", exception.Message);
    }

    #endregion

    #region Error Context Tests

    [Fact]
    public void Read_ErrorWithRequestId_IncludesRequestIdInException()
    {
        // Arrange
        var json = """
        {
            "jsonrpc": "1.0",
            "id": "error-test-id",
            "method": "tasks/get"
        }
        """;

        // Act & Assert
        var exception = Assert.Throws<A2AException>(() =>
            JsonSerializer.Deserialize<JsonRpcRequest>(json, _options));

        Assert.Equal("error-test-id", exception.GetRequestId());
    }

    [Fact]
    public void Read_ErrorWithoutRequestId_HasNullRequestId()
    {
        // Arrange
        var json = """
        {
            "jsonrpc": "1.0",
            "method": "tasks/get"
        }
        """;

        // Act & Assert
        var exception = Assert.Throws<A2AException>(() =>
            JsonSerializer.Deserialize<JsonRpcRequest>(json, _options));

        Assert.Null(exception.GetRequestId());
    }

    #endregion

    #region Serialization Tests

    [Fact]
    public void Write_ValidJsonRpcRequest_WithAllFields_WritesCorrectJson()
    {
        // Arrange
        using var paramsDoc = JsonDocument.Parse("""{"key": "value"}""");
        var request = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Id = "test-id",
            Method = "message/send",
            Params = paramsDoc.RootElement
        };

        // Act
        var json = JsonSerializer.Serialize(request, _options);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal("test-id", root.GetProperty("id").GetString());
        Assert.Equal("message/send", root.GetProperty("method").GetString());
        Assert.Equal("value", root.GetProperty("params").GetProperty("key").GetString());
    }

    [Fact]
    public void Write_ValidJsonRpcRequest_WithoutParams_WritesCorrectJson()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Id = "test-id",
            Method = "tasks/get",
            Params = null
        };

        // Act
        var json = JsonSerializer.Serialize(request, _options);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal("test-id", root.GetProperty("id").GetString());
        Assert.Equal("tasks/get", root.GetProperty("method").GetString());
        Assert.False(root.TryGetProperty("params", out _));
    }

    [Fact]
    public void Write_ValidJsonRpcRequest_WithNullId_WritesCorrectJson()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Id = null,
            Method = "tasks/get",
            Params = null
        };

        // Act
        var json = JsonSerializer.Serialize(request, _options);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("id").ValueKind);
        Assert.Equal("tasks/get", root.GetProperty("method").GetString());
    }

    #endregion

    #region Round-trip Tests

    [Fact]
    public void RoundTrip_ValidJsonRpcRequest_PreservesAllData()
    {
        // Arrange
        using var paramsDoc = JsonDocument.Parse("""
        {
            "message": {
                "messageId": "msg-1",
                "role": "user",
                "parts": []
            }
        }
        """);

        var original = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Id = "round-trip-test",
            Method = "message/send",
            Params = paramsDoc.RootElement
        };

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<JsonRpcRequest>(json, _options);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.JsonRpc, deserialized.JsonRpc);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Method, deserialized.Method);
        Assert.True(deserialized.Params.HasValue);
        Assert.Equal("msg-1", deserialized.Params.Value.GetProperty("message").GetProperty("messageId").GetString());
    }

    [Theory]
    [InlineData("message/send")]
    [InlineData("message/stream")]
    [InlineData("tasks/get")]
    [InlineData("tasks/cancel")]
    [InlineData("tasks/subscribe")]
    [InlineData("tasks/pushNotificationConfig/set")]
    [InlineData("tasks/pushNotificationConfig/get")]
    public void RoundTrip_AllValidMethods_PreservesMethod(string method)
    {
        // Arrange
        var original = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Id = "method-test",
            Method = method,
            Params = null
        };

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<JsonRpcRequest>(json, _options);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(method, deserialized.Method);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void Read_ValidParamsNull_ReturnsRequestWithoutParams()
    {
        // Arrange
        var json = """
        {
            "jsonrpc": "2.0",
            "id": "test-id",
            "method": "tasks/get",
            "params": null
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<JsonRpcRequest>(json, _options);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Params.HasValue);
    }

    [Fact]
    public void Read_EmptyParamsObject_ReturnsRequestWithEmptyParams()
    {
        // Arrange
        var json = """
        {
            "jsonrpc": "2.0",
            "id": "test-id",
            "method": "tasks/get",
            "params": {}
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<JsonRpcRequest>(json, _options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Params.HasValue);
        Assert.Equal(JsonValueKind.Object, result.Params.Value.ValueKind);
    }

    #endregion
}
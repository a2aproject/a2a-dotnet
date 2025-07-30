using System.Text.Json.Nodes;

namespace A2A.UnitTests.JsonRpc;

public class JsonRpcErrorResponseTests
{
    [Fact]
    public void JsonRpcErrorResponse_Properties_SetAndGet()
    {
        // Arrange
        var error = new JsonRpcError { Code = 123, Message = "err" };

        // Act
        var sut = new JsonRpcResponse
        {
            Id = "id1",
            JsonRpc = "2.0",
            Error = error
        };

        // Assert
        Assert.Equal("id1", sut.Id);
        Assert.Equal("2.0", sut.JsonRpc);
        Assert.Equal(error, sut.Error);
    }

    [Fact]
    public void JsonRpcErrorResponse_CanSetResult()
    {
        // Arrange
        var node = JsonValue.Create(42);

        // Act
        var sut = new JsonRpcResponse { Result = node };

        // Assert
        Assert.Equal(42, sut.Result!.GetValue<int>());
    }

    [Fact]
    public void CreateJsonRpcErrorResponse_WithValidException_CreatesCorrectResponse()
    {
        // Arrange
        const string requestId = "test-request-123";
        const string errorMessage = "Test error message";
        const A2AErrorCode errorCode = A2AErrorCode.InvalidParams;
        var exception = new A2AException(errorMessage, errorCode);

        // Act
        var response = JsonRpcResponse.CreateJsonRpcErrorResponse(requestId, exception);

        // Assert
        Assert.Equal(requestId, response.Id);
        Assert.Equal("2.0", response.JsonRpc);
        Assert.Null(response.Result);
        Assert.NotNull(response.Error);
        Assert.Equal((int)errorCode, response.Error.Code);
        Assert.Equal(errorMessage, response.Error.Message);
    }

    [Fact]
    public void CreateJsonRpcErrorResponse_WithNullRequestId_CreatesCorrectResponse()
    {
        // Arrange
        const string errorMessage = "Test error message";
        const A2AErrorCode errorCode = A2AErrorCode.MethodNotFound;
        var exception = new A2AException(errorMessage, errorCode);

        // Act
        var response = JsonRpcResponse.CreateJsonRpcErrorResponse(new JsonRpcId((string?)null), exception);

        // Assert
        Assert.False(response.Id.HasValue);
        Assert.Equal("2.0", response.JsonRpc);
        Assert.Null(response.Result);
        Assert.NotNull(response.Error);
        Assert.Equal((int)errorCode, response.Error.Code);
        Assert.Equal(errorMessage, response.Error.Message);
    }
}

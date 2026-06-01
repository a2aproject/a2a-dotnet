using System.Text.Json;
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
        Assert.Null(response.Error.Data);
    }

    [Fact]
    public void CreateJsonRpcErrorResponse_WithA2ASpecificError_PopulatesDataWithErrorInfo()
    {
        // Arrange
        const string requestId = "test-request-456";
        const string errorMessage = "Task not found";
        const A2AErrorCode errorCode = A2AErrorCode.TaskNotFound;
        var exception = new A2AException(errorMessage, errorCode);

        // Act
        var response = JsonRpcResponse.CreateJsonRpcErrorResponse(requestId, exception);

        // Assert
        Assert.NotNull(response.Error);
        Assert.NotNull(response.Error.Data);
        AssertErrorInfoData(response.Error.Data.Value, "TASK_NOT_FOUND");
    }

    [Fact]
    public void CreateJsonRpcErrorResponse_WithStandardError_LeavesDataNull()
    {
        // Arrange
        const string requestId = "test-request-789";
        const string errorMessage = "Internal error";
        const A2AErrorCode errorCode = A2AErrorCode.InternalError;
        var exception = new A2AException(errorMessage, errorCode);

        // Act
        var response = JsonRpcResponse.CreateJsonRpcErrorResponse(requestId, exception);

        // Assert
        Assert.NotNull(response.Error);
        Assert.Null(response.Error.Data);
    }

    [Fact]
    public void TaskNotFoundResponse_PopulatesDataWithErrorInfo()
    {
        // Act
        var response = JsonRpcResponse.TaskNotFoundResponse("req-1");

        // Assert
        Assert.NotNull(response.Error);
        Assert.NotNull(response.Error.Data);
        AssertErrorInfoData(response.Error.Data.Value, "TASK_NOT_FOUND");
    }

    [Fact]
    public void InternalErrorResponse_LeavesDataNull()
    {
        // Act
        var response = JsonRpcResponse.InternalErrorResponse("req-2");

        // Assert
        Assert.NotNull(response.Error);
        Assert.Null(response.Error.Data);
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

    private static void AssertErrorInfoData(JsonElement data, string expectedReason)
    {
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        Assert.Equal(1, data.GetArrayLength());

        var element = data[0];
        Assert.Equal("type.googleapis.com/google.rpc.ErrorInfo", element.GetProperty("@type").GetString());
        Assert.Equal(expectedReason, element.GetProperty("reason").GetString());
        Assert.Equal("a2a-protocol.org", element.GetProperty("domain").GetString());
    }
}

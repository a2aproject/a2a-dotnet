namespace A2A.V0_3.UnitTests.JsonRpc;

public class ErrorTypesTests
{
    [Fact]
    public void InternalError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = JsonRpcResponse.InternalErrorResponse("123");

        // Assert
        Assert.Equal("123", sut.Id);
        Assert.Equal(-32603, sut.Error?.Code);
        Assert.Equal("Internal error", sut.Error?.Message);
    }

    [Fact]
    public void TaskNotFoundError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = JsonRpcResponse.TaskNotFoundResponse("123");

        // Assert
        Assert.Equal("123", sut.Id);
        Assert.Equal(-32001, sut.Error?.Code);
        Assert.Equal("Task not found", sut.Error?.Message);
    }

    [Fact]
    public void TaskNotCancelableError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = JsonRpcResponse.TaskNotCancelableResponse("123");

        // Assert
        Assert.Equal("123", sut.Id);
        Assert.Equal(-32002, sut.Error?.Code);
        Assert.Equal("Task cannot be canceled", sut.Error?.Message);
    }

    [Fact]
    public void PushNotificationNotSupportedError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = JsonRpcResponse.PushNotificationNotSupportedResponse("123");

        // Assert
        Assert.Equal("123", sut.Id);
        Assert.Equal(-32003, sut.Error?.Code);
        Assert.Equal("Push notification not supported", sut.Error?.Message);
    }

    [Fact]
    public void UnsupportedOperationError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = JsonRpcResponse.UnsupportedOperationResponse("123");

        // Assert
        Assert.Equal("123", sut.Id);
        Assert.Equal(-32004, sut.Error?.Code);
        Assert.Equal("Unsupported operation", sut.Error?.Message);
    }

    [Fact]
    public void ContentTypeNotSupportedError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = JsonRpcResponse.ContentTypeNotSupportedResponse("123");

        // Assert
        Assert.Equal("123", sut.Id);
        Assert.Equal(-32005, sut.Error?.Code);
        Assert.Equal("Content type not supported", sut.Error?.Message);
    }

    [Fact]
    public void MethodNotFoundError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = JsonRpcResponse.MethodNotFoundResponse("123");

        // Assert
        Assert.Equal("123", sut.Id);
        Assert.Equal(-32601, sut.Error?.Code);
        Assert.Equal("Method not found", sut.Error?.Message);
    }

    [Fact]
    public void ParseError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = JsonRpcResponse.ParseErrorResponse("123");

        // Assert
        Assert.Equal("123", sut.Id);
        Assert.Equal(-32700, sut.Error?.Code);
        Assert.Equal("Invalid JSON payload", sut.Error?.Message);
    }

    [Fact]
    public void InvalidParamsError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = JsonRpcResponse.InvalidParamsResponse("123");

        // Assert
        Assert.Equal("123", sut.Id);
        Assert.Equal(-32602, sut.Error?.Code);
        Assert.Equal("Invalid parameters", sut.Error?.Message);
    }
}

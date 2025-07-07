using System.Net;

namespace A2A.UnitTests.JsonRpc;

public class ErrorTypesTests
{
    [Fact]
    public void A2AClientError_ConstructsWithMessageAndInnerException()
    {
        // Arrange
        var inner = new InvalidOperationException("inner");

        // Act
        var sut = new A2AClientException("msg", inner);

        // Assert
        Assert.Equal("msg", sut.Message);
        Assert.Equal(inner, sut.InnerException);
    }

    [Fact]
    public void A2AClientHTTPError_PropertiesSetCorrectly()
    {
        // Act
        var sut = new A2AClientHTTPException(HttpStatusCode.NotFound, "Not Found");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, sut.StatusCode);
        Assert.Equal("Not Found", sut.ErrorMessage);
        Assert.Contains("404", sut.Message);
        Assert.Contains("Not Found", sut.Message);
    }

    [Fact]
    public void A2AClientJsonError_PropertiesSetCorrectly()
    {
        // Act
        var sut = new A2AClientJsonException("bad json");

        // Assert
        Assert.Equal("bad json", sut.ErrorMessage);
        Assert.Contains("bad json", sut.Message);
    }

    [Fact]
    public void MissingAPIKeyError_HasExpectedMessage()
    {
        // Act
        var sut = new MissingAPIKeyException();

        // Assert
        Assert.Equal("API key is required but was not provided", sut.Message);
    }

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
}
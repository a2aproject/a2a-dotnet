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
        var sut = new A2AClientHTTPException(404, "Not Found");

        // Assert
        Assert.Equal(404, sut.StatusCode);
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
        var sut = new InternalError();

        // Assert
        Assert.Equal(-32603, sut.Code);
        Assert.Equal("Internal error", sut.Message);
    }

    [Fact]
    public void TaskNotFoundError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = new TaskNotFoundError();

        // Assert
        Assert.Equal(-32001, sut.Code);
        Assert.Equal("Task not found", sut.Message);
    }

    [Fact]
    public void TaskNotCancelableError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = new TaskNotCancelableError();

        // Assert
        Assert.Equal(-32002, sut.Code);
        Assert.Equal("Task cannot be canceled", sut.Message);
    }

    [Fact]
    public void PushNotificationNotSupportedError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = new PushNotificationNotSupportedError();

        // Assert
        Assert.Equal(-32003, sut.Code);
        Assert.Equal("Push Notification is not supported", sut.Message);
    }

    [Fact]
    public void UnsupportedOperationError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = new UnsupportedOperationError();

        // Assert
        Assert.Equal(-32004, sut.Code);
        Assert.Equal("This operation is not supported", sut.Message);
    }

    [Fact]
    public void ContentTypeNotSupportedError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = new ContentTypeNotSupportedError();

        // Assert
        Assert.Equal(-32005, sut.Code);
        Assert.Equal("Incompatible content types", sut.Message);
    }
}
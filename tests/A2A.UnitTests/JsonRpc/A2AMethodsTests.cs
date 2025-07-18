namespace A2A.UnitTests.JsonRpc;

public class A2AMethodsTests
{
    [Fact]
    public void IsStreamingMethod_ReturnsTrue_ForMessageStream()
    {
        // Arrange
        var method = A2AMethods.MessageStream;

        // Act
        var result = A2AMethods.IsStreamingMethod(method);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsStreamingMethod_ReturnsTrue_ForTaskResubscribe()
    {
        // Arrange
        var method = A2AMethods.TaskResubscribe;

        // Act
        var result = A2AMethods.IsStreamingMethod(method);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData(A2AMethods.MessageSend)]
    [InlineData(A2AMethods.TaskGet)]
    [InlineData(A2AMethods.TaskCancel)]
    [InlineData(A2AMethods.TaskPushNotificationConfigSet)]
    [InlineData(A2AMethods.TaskPushNotificationConfigGet)]
    [InlineData("unknown/method")]
    public void IsStreamingMethod_ReturnsFalse_ForNonStreamingMethods(string method)
    {
        // Act
        var result = A2AMethods.IsStreamingMethod(method);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(A2AMethods.MessageSend)]
    [InlineData(A2AMethods.MessageStream)]
    [InlineData(A2AMethods.TaskGet)]
    [InlineData(A2AMethods.TaskCancel)]
    [InlineData(A2AMethods.TaskResubscribe)]
    [InlineData(A2AMethods.TaskPushNotificationConfigSet)]
    [InlineData(A2AMethods.TaskPushNotificationConfigGet)]
    public void IsValidMethod_ReturnsTrue_ForValidMethods(string method)
    {
        // Act
        var result = A2AMethods.IsValidMethod(method);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("unknown/method")]
    [InlineData("message/ssend")]
    [InlineData("invalid")]
    [InlineData("")]
    public void IsValidMethod_ReturnsFalse_ForInvalidMethods(string method)
    {
        // Act
        var result = A2AMethods.IsValidMethod(method);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidMethod_ReturnsFalse_ForNullMethod()
    {
        // Act
        var result = A2AMethods.IsValidMethod(null!);

        // Assert
        Assert.False(result);
    }
}
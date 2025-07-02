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
}
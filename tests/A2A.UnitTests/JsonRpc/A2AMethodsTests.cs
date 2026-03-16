using System.Reflection;

namespace A2A.UnitTests.JsonRpc;

public class A2AMethodsTests
{
    [Fact]
    public void IsStreamingMethod_ReturnsTrue_ForSendStreamingMessage()
    {
        // Arrange
        var method = A2AMethods.SendStreamingMessage;

        // Act
        var result = A2AMethods.IsStreamingMethod(method);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsStreamingMethod_ReturnsTrue_ForSubscribeToTask()
    {
        // Arrange
        var method = A2AMethods.SubscribeToTask;

        // Act
        var result = A2AMethods.IsStreamingMethod(method);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData(A2AMethods.SendMessage)]
    [InlineData(A2AMethods.GetTask)]
    [InlineData(A2AMethods.CancelTask)]
    [InlineData(A2AMethods.ListTasks)]
    [InlineData(A2AMethods.CreateTaskPushNotificationConfig)]
    [InlineData(A2AMethods.GetTaskPushNotificationConfig)]
    [InlineData("unknown/method")]
    public void IsStreamingMethod_ReturnsFalse_ForNonStreamingMethods(string method)
    {
        // Act
        var result = A2AMethods.IsStreamingMethod(method);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("unknown/method")]
    [InlineData("message/send")]
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

    [Fact]
    public void IsValidMethod_ReturnsTrue_ForAllDefinedMethods()
    {
        // Arrange: Use reflection to get all const string fields from A2AMethods
        var methodFields = typeof(A2AMethods)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToList();

        // Assert we found some methods (sanity check)
        Assert.NotEmpty(methodFields);

        // Act & Assert: Each method constant should be valid
        foreach (var field in methodFields)
        {
            var methodValue = (string)field.GetValue(null)!;
            var isValid = A2AMethods.IsValidMethod(methodValue);

            Assert.True(isValid, $"Method '{methodValue}' (from field '{field.Name}') should be valid but IsValidMethod returned false. " +
                                 "This likely means the method constant was added to A2AMethods but not included in the IsValidMethod implementation.");
        }
    }
}
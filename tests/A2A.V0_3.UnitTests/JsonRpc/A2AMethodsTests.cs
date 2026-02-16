using System.Reflection;

namespace A2A.V0_3.UnitTests.JsonRpc;

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
    public void IsStreamingMethod_ReturnsTrue_ForTaskSubscribe()
    {
        // Arrange
        var method = A2AMethods.TaskSubscribe;

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

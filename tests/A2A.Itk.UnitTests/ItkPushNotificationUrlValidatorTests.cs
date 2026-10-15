namespace A2A.Itk.UnitTests;

using A2A;
using System.Net;

public sealed class ItkPushNotificationUrlValidatorTests
{
    [Fact]
    public async Task ValidateAsync_AcceptsHttpIpv4LoopbackReceiver()
    {
        var validator = CreateValidator();

        var addresses = await validator.ValidateAsync("http://127.0.0.1:1234/notifications");

        Assert.Equal([IPAddress.Loopback], addresses);
    }

    [Theory]
    [InlineData("https://127.0.0.1:1234/notifications")]
    [InlineData("http://localhost:1234/notifications")]
    [InlineData("http://192.0.2.1:1234/notifications")]
    [InlineData("http://[::1]:1234/notifications")]
    public async Task ValidateAsync_RejectsNonItkReceiver(string url)
    {
        var validator = CreateValidator();

        var exception = await Assert.ThrowsAsync<A2AException>(
            () => validator.ValidateAsync(url));

        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
    }

    private static IPushNotificationUrlValidator CreateValidator()
    {
        var validatorType = typeof(ItkAgent).Assembly.GetType(
            "A2A.Itk.ItkPushNotificationUrlValidator");

        Assert.NotNull(validatorType);
        return Assert.IsAssignableFrom<IPushNotificationUrlValidator>(
            Activator.CreateInstance(validatorType, nonPublic: true));
    }
}

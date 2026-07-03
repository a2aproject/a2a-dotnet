namespace A2A.Grpc.UnitTests;

using A2A;
using global::Grpc.Net.Client;

/// <summary>Null-parameter validation tests for the public gRPC client surface.</summary>
public class ArgumentValidationTests
{
    [Fact]
    public void Constructor_NullUri_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new A2AGrpcClient((Uri)null!));
    }

    [Fact]
    public void Constructor_NullChannel_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new A2AGrpcClient((GrpcChannel)null!));
    }

    [Fact]
    public async Task UnaryMethods_NullRequest_Throw()
    {
        using var client = new A2AGrpcClient(new Uri("https://localhost:5001"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendMessageAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetTaskAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.ListTasksAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.CancelTaskAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.CreateTaskPushNotificationConfigAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetTaskPushNotificationConfigAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.ListTaskPushNotificationConfigAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.DeleteTaskPushNotificationConfigAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetExtendedAgentCardAsync(null!));
    }

    [Fact]
    public void StreamingMethods_NullRequest_Throw()
    {
        using var client = new A2AGrpcClient(new Uri("https://localhost:5001"));

        Assert.Throws<ArgumentNullException>(() => client.SendStreamingMessageAsync(null!));
        Assert.Throws<ArgumentNullException>(() => client.SubscribeToTaskAsync(null!));
    }
}

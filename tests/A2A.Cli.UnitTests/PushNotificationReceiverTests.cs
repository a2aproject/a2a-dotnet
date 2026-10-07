using System.Net;
using System.Text;
using System.Text.Json;

namespace A2A.Cli.Tests;

public sealed class PushNotificationReceiverTests
{
    [Fact]
    public async Task Main_ReceiverStartupFailure_ReturnsNonZeroExitCode()
    {
        int exitCode = await A2ACli.Main(
        [
            "--agent", "http://127.0.0.1:1",
            "--use-push-notifications",
            "--push-notification-receiver", "http://192.0.2.1:5000"
        ]);

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task StartAsync_ReceivesStreamResponse()
    {
        var received = new TaskCompletionSource<StreamResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var receiver = await PushNotificationReceiver.StartAsync(
            new Uri("http://127.0.0.1:0"),
            notification => received.TrySetResult(notification),
            CancellationToken.None);
        using var client = new HttpClient();
        var notification = new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = "task-1",
                ContextId = "context-1",
                Status = new TaskStatus { State = TaskState.Completed }
            }
        };

        using var response = await client.PostAsync(
            receiver.NotificationUri,
            CreateJsonContent(notification),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(StreamResponseCase.StatusUpdate, actual.PayloadCase);
        Assert.Equal("task-1", actual.StatusUpdate?.TaskId);
    }

    [Fact]
    public async Task StartAsync_MalformedJson_ReturnsBadRequest()
    {
        var notificationReceived = false;
        await using var receiver = await PushNotificationReceiver.StartAsync(
            new Uri("http://127.0.0.1:0"),
            _ => notificationReceived = true,
            CancellationToken.None);
        using var client = new HttpClient();

        using var response = await client.PostAsync(
            receiver.NotificationUri,
            new StringContent("{", Encoding.UTF8, "application/json"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(notificationReceived);
    }

    [Fact]
    public async Task StartAsync_BasePath_AppendsNotifyPath()
    {
        await using var receiver = await PushNotificationReceiver.StartAsync(
            new Uri("http://127.0.0.1:0/callbacks/"),
            _ => { },
            CancellationToken.None);

        Assert.Equal("/callbacks/notify", receiver.NotificationUri.AbsolutePath);
        Assert.NotEqual(0, receiver.NotificationUri.Port);
    }

    [Fact]
    public async Task DisposeAsync_StopsReceiver()
    {
        var receiver = await PushNotificationReceiver.StartAsync(
            new Uri("http://127.0.0.1:0"),
            _ => { },
            CancellationToken.None);
        using var client = new HttpClient();
        Uri notificationUri = receiver.NotificationUri;

        await receiver.DisposeAsync();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.PostAsync(
                notificationUri,
                CreateJsonContent(new StreamResponse()),
                CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_NonLoopbackAddress_Throws()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => PushNotificationReceiver.StartAsync(
                new Uri("http://192.0.2.1:5000"),
                _ => { },
                CancellationToken.None));

        Assert.Contains("loopback", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static StringContent CreateJsonContent(StreamResponse notification) =>
        new(
            JsonSerializer.Serialize(notification, A2AJsonUtilities.DefaultOptions),
            Encoding.UTF8,
            "application/json");
}

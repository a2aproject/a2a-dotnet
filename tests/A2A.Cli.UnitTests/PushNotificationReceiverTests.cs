using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace A2A.Cli.Tests;

public sealed class PushNotificationReceiverTests : IAsyncLifetime
{
    private readonly PushNotificationSessionTests.TestClock _clock = new();
    private readonly A2AClient _agentClient = new(new Uri("http://localhost"));
    private PushNotificationSession _session = null!;

    public Task InitializeAsync()
    {
        _session = new PushNotificationSession(_agentClient, NullLogger.Instance, _clock);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        _agentClient.Dispose();
    }

    [Fact]
    public async Task StartAsync_MissingCredentials_ReturnsUnauthorized()
    {
        var notificationReceived = false;
        await using var receiver = await PushNotificationReceiver.StartAsync(
            new Uri("http://127.0.0.1:0"),
            _ => notificationReceived = true,
            _session, CancellationToken.None);
        using var client = new HttpClient();

        using var response = await client.PostAsync(
            receiver.NotificationUri,
            CreateJsonContent(new StreamResponse()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(notificationReceived);
    }

    [Theory]
    [InlineData("Bearer", "incorrect")]
    [InlineData("Basic", "incorrect")]
    public async Task StartAsync_InvalidCredentials_ReturnsUnauthorized(string scheme, string credential)
    {
        await using var receiver = await PushNotificationReceiver.StartAsync(
            new Uri("http://127.0.0.1:0"), _ => Assert.Fail("Unauthorized notification was delivered."),
            _session, CancellationToken.None);
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new(scheme, credential);

        using var response = await client.PostAsync(receiver.NotificationUri, CreateJsonContent(new StreamResponse()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StartAsync_ExpiredCredentials_ReturnsUnauthorized()
    {
        await using var receiver = await PushNotificationReceiver.StartAsync(
            new Uri("http://127.0.0.1:0"), _ => Assert.Fail("Expired notification was delivered."),
            _session, CancellationToken.None);
        using var client = new HttpClient();
        Authorize(client, receiver);
        _clock.Advance(TimeSpan.FromMinutes(5));

        using var response = await client.PostAsync(receiver.NotificationUri, CreateJsonContent(new StreamResponse()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Main_ReceiverStartupFailure_ReturnsNonZeroExitCode()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        await using var agent = builder.Build();
        agent.Urls.Add("http://127.0.0.1:0");
        var cardWasRequested = false;
        agent.MapGet("/.well-known/agent-card.json", () =>
        {
            cardWasRequested = true;
            return Results.Json(new AgentCard
            {
                Name = "Test agent",
                Description = "Test agent",
                Version = "1.0",
                SupportedInterfaces = [new AgentInterface { Url = "http://127.0.0.1:1", ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" }]
            }, A2AJsonUtilities.DefaultOptions);
        });
        await agent.StartAsync();

        int exitCode = await A2ACli.Main(
        [
            "--agent", agent.Urls.Single(),
            "--use-push-notifications",
            "--push-notification-receiver", "http://192.0.2.1:5000"
        ]);

        Assert.True(cardWasRequested);
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
            _session, CancellationToken.None);
        using var client = new HttpClient();
        Authorize(client, receiver);
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
            _session, CancellationToken.None);
        using var client = new HttpClient();
        Authorize(client, receiver);

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
            _session, CancellationToken.None);

        Assert.Equal("/callbacks/notify", receiver.NotificationUri.AbsolutePath);
        Assert.NotEqual(0, receiver.NotificationUri.Port);
    }

    [Fact]
    public async Task DisposeAsync_StopsReceiver()
    {
        var receiver = await PushNotificationReceiver.StartAsync(
            new Uri("http://127.0.0.1:0"),
            _ => { },
            _session, CancellationToken.None);
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
                _session, CancellationToken.None));

        Assert.Contains("loopback", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static StringContent CreateJsonContent(StreamResponse notification) =>
        new(
            JsonSerializer.Serialize(notification, A2AJsonUtilities.DefaultOptions),
            Encoding.UTF8,
            "application/json");

    private void Authorize(HttpClient client, PushNotificationReceiver receiver)
    {
        var config = _session.CreateConfiguration(receiver.NotificationUri);
        client.DefaultRequestHeaders.Authorization = new("Bearer", config.Authentication!.Credentials);
    }
}

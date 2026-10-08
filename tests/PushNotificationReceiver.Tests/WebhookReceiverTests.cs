using A2A;
using Microsoft.AspNetCore.Builder;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PushNotificationReceiver.Tests;

[CollectionDefinition("Receiver hosting configuration", DisableParallelization = true)]
public sealed class ReceiverHostingConfigurationDefinition;

[Collection("Receiver hosting configuration")]
public sealed class WebhookReceiverTests : IAsyncLifetime
{
    private const string TaskId = "receiver-test-task";
    private readonly string _contextId = Guid.NewGuid().ToString("N");
    private readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(10);
    private readonly string _credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly ConcurrentQueue<StreamResponse> _received = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _app = new WebhookReceiver(TaskId, _credential, _received.Enqueue).CreateApplication();
        await _app.StartAsync();
        _client = CreateClient(_app);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task HostingEnvironment_DoesNotOverrideTheOwnedEndpoint()
    {
        var overrides = new Dictionary<string, string>
        {
            ["ASPNETCORE_URLS"] = "http://127.0.0.2:0",
            ["ASPNETCORE_PREFERHOSTINGURLS"] = "true",
            ["Kestrel__Endpoints__Ignored__Url"] = "http://127.0.0.2:0",
        };
        var original = overrides.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        WebApplication app;
        try
        {
            foreach (var setting in overrides)
            {
                Environment.SetEnvironmentVariable(setting.Key, setting.Value);
            }
            app = new WebhookReceiver(TaskId, _credential, _received.Enqueue).CreateApplication();
        }
        finally
        {
            foreach (var setting in original)
            {
                Environment.SetEnvironmentVariable(setting.Key, setting.Value);
            }
        }

        await using (app)
        {
            await app.StartAsync();
            var address = new Uri(Assert.Single(app.Urls));
            Assert.Equal("127.0.0.1", address.Host);
            Assert.NotEqual(0, address.Port);
        }
    }

    [Theory]
    [InlineData(StreamResponseCase.Task)]
    [InlineData(StreamResponseCase.Message)]
    [InlineData(StreamResponseCase.StatusUpdate)]
    [InlineData(StreamResponseCase.ArtifactUpdate)]
    public async Task EachPayloadCase_IsReceivedOverLoopback(StreamResponseCase payloadCase)
    {
        var notification = CreateNotification(payloadCase);
        using var request = CreateRequest(notification);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var received = Assert.Single(_received);
        Assert.Equal(payloadCase, received.PayloadCase);
        Assert.Equal(
            JsonSerializer.Serialize(notification, A2AJsonUtilities.DefaultOptions),
            JsonSerializer.Serialize(received, A2AJsonUtilities.DefaultOptions));
        Assert.Equal("127.0.0.1", _client.BaseAddress!.Host);
        Assert.NotEqual(0, _client.BaseAddress.Port);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer wrong-credential")]
    [InlineData("Basic wrong-credential")]
    public async Task MissingOrWrongAuthorization_IsRejected(string? authorization)
    {
        using var request = CreateRequest(CreateNotification(StreamResponseCase.Task));
        request.Headers.Authorization = authorization is null ? null : AuthenticationHeaderValue.Parse(authorization);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_received);
    }

    [Fact]
    public async Task LegacyToken_DoesNotReplaceBearerAuthentication()
    {
        using var request = CreateRequest(CreateNotification(StreamResponseCase.Task));
        request.Headers.Authorization = null;
        request.Headers.Add("X-A2A-Notification-Token", _credential);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_received);
    }

    [Fact]
    public async Task ConfiguredLegacyToken_IsRequiredInAdditionToBearer()
    {
        var legacyToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var received = new ConcurrentQueue<StreamResponse>();
        await using var app = new WebhookReceiver(TaskId, _credential, received.Enqueue, legacyToken).CreateApplication();
        await app.StartAsync();
        using var client = CreateClient(app);

        foreach (var token in new[] { null, "wrong-token", legacyToken })
        {
            using var request = CreateRequest(CreateNotification(StreamResponseCase.StatusUpdate));
            if (token is not null)
            {
                request.Headers.Add("X-A2A-Notification-Token", token);
            }

            using var response = await client.SendAsync(request);
            var expected = token == legacyToken ? HttpStatusCode.NoContent : HttpStatusCode.Unauthorized;
            Assert.Equal(expected, response.StatusCode);
        }

        Assert.Single(received);
    }

    [Theory]
    [InlineData(StreamResponseCase.Task)]
    [InlineData(StreamResponseCase.Message)]
    [InlineData(StreamResponseCase.StatusUpdate)]
    [InlineData(StreamResponseCase.ArtifactUpdate)]
    public async Task OtherTask_IsRejected(StreamResponseCase payloadCase)
    {
        using var request = CreateRequest(CreateNotification(payloadCase, "another-task"));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_received);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("""{"statusUpdate":{"taskId":"receiver-test-task"}}""")]
    [InlineData("""{"statusUpdate":{"taskId":"receiver-test-task","contextId":"ctx","status":null}}""")]
    [InlineData("""{"id":"receiver-test-task","contextId":"ctx","status":{"state":"TASK_STATE_COMPLETED"}}""")]
    public async Task MalformedOrUnwrappedPayload_IsRejected(string json)
    {
        using var request = CreateRequest(CreateNotification(StreamResponseCase.Task));
        request.Content!.Dispose();
        request.Content = new StringContent(json, Encoding.UTF8, "application/a2a+json");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_received);
    }

    [Fact]
    public async Task MultiplePayloadFields_AreRejected()
    {
        var notification = CreateNotification(StreamResponseCase.Task);
        notification.Message = CreateNotification(StreamResponseCase.Message).Message;
        using var request = CreateRequest(notification);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_received);
    }

    [Fact]
    public async Task MessageWithoutTaskId_IsRejected()
    {
        var notification = CreateNotification(StreamResponseCase.Message);
        notification.Message!.TaskId = null;
        using var request = CreateRequest(notification);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_received);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/a2a+json; charset=utf-16")]
    public async Task UnsupportedContentType_IsRejected(string contentType)
    {
        using var request = CreateRequest(CreateNotification(StreamResponseCase.Task));
        request.Content!.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(_received);
    }

    [Theory]
    [InlineData("application/a2a+json")]
    [InlineData("application/json")]
    [InlineData("Application/A2A+Json; charset=UTF-8")]
    [InlineData("application/a2a+json; charset=\"utf-8\"")]
    public async Task SupportedContentType_IsAccepted(string contentType)
    {
        using var request = CreateRequest(CreateNotification(StreamResponseCase.Task));
        request.Content!.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(_received);
    }

    [Fact]
    public async Task OversizedPayload_IsRejected()
    {
        var notification = CreateNotification(StreamResponseCase.Message);
        notification.Message!.Parts = [Part.FromText(new string('x', 64 * 1024))];
        using var request = CreateRequest(notification);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(_received);
    }

    [Fact]
    public async Task RepeatedTerminalNotification_IsAcknowledgedAgain()
    {
        var notification = CreateNotification(StreamResponseCase.StatusUpdate);
        notification.StatusUpdate!.Status.State = TaskState.Completed;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var request = CreateRequest(notification);
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal(2, _received.Count);
        Assert.All(_received, item => Assert.Equal(TaskState.Completed, item.StatusUpdate!.Status.State));
    }

    private HttpClient CreateClient(WebApplication app)
    {
        return new HttpClient(new HttpClientHandler
        {
            UseProxy = false,
            UseCookies = false,
            AllowAutoRedirect = false,
        })
        {
            BaseAddress = new Uri(app.Urls.Single()),
            Timeout = _requestTimeout,
        };
    }

    private HttpRequestMessage CreateRequest(StreamResponse notification)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/notifications")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(notification, A2AJsonUtilities.DefaultOptions),
                Encoding.UTF8,
                "application/a2a+json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _credential);
        return request;
    }

    private StreamResponse CreateNotification(StreamResponseCase payloadCase, string taskId = TaskId)
    {
        return payloadCase switch
        {
            StreamResponseCase.Task => new StreamResponse
            {
                Task = new AgentTask
                {
                    Id = taskId,
                    ContextId = _contextId,
                    Status = new A2A.TaskStatus { State = TaskState.Submitted },
                },
            },
            StreamResponseCase.Message => new StreamResponse
            {
                Message = new Message
                {
                    MessageId = "notification-message",
                    TaskId = taskId,
                    ContextId = _contextId,
                    Role = Role.Agent,
                    Parts = [Part.FromText("local message")],
                },
            },
            StreamResponseCase.StatusUpdate => new StreamResponse
            {
                StatusUpdate = new TaskStatusUpdateEvent
                {
                    TaskId = taskId,
                    ContextId = _contextId,
                    Status = new A2A.TaskStatus { State = TaskState.Working },
                },
            },
            StreamResponseCase.ArtifactUpdate => new StreamResponse
            {
                ArtifactUpdate = new TaskArtifactUpdateEvent
                {
                    TaskId = taskId,
                    ContextId = _contextId,
                    Artifact = new Artifact { ArtifactId = "local-artifact", Parts = [Part.FromText("local result")] },
                    LastChunk = true,
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(payloadCase)),
        };
    }

}

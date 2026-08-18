namespace A2A.Grpc.UnitTests;

using A2A;
using global::Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// End-to-end tests exercising <see cref="A2AGrpcClient"/> against <c>MapGrpcA2A</c> over an in-memory
/// ASP.NET Core <see cref="TestServer"/>, validating transport, mapping and error propagation.
/// </summary>
public sealed class GrpcIntegrationTests : IAsyncLifetime
{
    private readonly FakeRequestHandler _handler = new();
    private WebApplication? _app;
    private GrpcChannel? _channel;
    private A2AGrpcClient? _client;

    private A2AGrpcClient Client => _client!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGrpc();
        builder.Services.AddSingleton<IA2ARequestHandler>(_handler);

        _app = builder.Build();
        _app.MapGrpcA2A();
        await _app.StartAsync();

        var testServer = _app.GetTestServer();
        _channel = GrpcChannel.ForAddress(testServer.BaseAddress, new GrpcChannelOptions { HttpHandler = testServer.CreateHandler() });
        _client = new A2AGrpcClient(_channel);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        _channel?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task SendMessage_RoundTripsThroughHandler()
    {
        _handler.SendMessageResult = new SendMessageResponse { Task = new AgentTask { Id = "t7", ContextId = "c7" } };

        var response = await Client.SendMessageAsync(new SendMessageRequest
        {
            Message = new Message { MessageId = "m1", Role = Role.User, Parts = { Part.FromText("hello") } },
        });

        Assert.Equal(SendMessageResponseCase.Task, response.PayloadCase);
        Assert.Equal("t7", response.Task!.Id);
        Assert.Equal("hello", _handler.LastSendMessage!.Message.Parts[0].Text);
    }

    [Fact]
    public async Task GetTask_PassesRequestAndMapsResult()
    {
        _handler.TaskResult = new AgentTask { Id = "abc", ContextId = "ctx", Status = new TaskStatus { State = TaskState.Completed } };

        var task = await Client.GetTaskAsync(new GetTaskRequest { Id = "abc", HistoryLength = 5 });

        Assert.Equal("abc", task.Id);
        Assert.Equal(TaskState.Completed, task.Status.State);
        Assert.Equal("abc", _handler.LastGetTask!.Id);
        Assert.Equal(5, _handler.LastGetTask.HistoryLength);
    }

    [Fact]
    public async Task CreateAndDeletePushConfig_RoundTrip()
    {
        _handler.PushConfigResult = new TaskPushNotificationConfig
        {
            Id = "cfg9",
            TaskId = "task9",
            PushNotificationConfig = new PushNotificationConfig { Url = "https://hook", Token = "tok" },
        };

        var created = await Client.CreateTaskPushNotificationConfigAsync(new CreateTaskPushNotificationConfigRequest
        {
            TaskId = "task9",
            ConfigId = "cfg9",
            Config = new PushNotificationConfig { Url = "https://hook", Token = "tok" },
        });

        Assert.Equal("cfg9", created.Id);
        Assert.Equal("https://hook", created.PushNotificationConfig.Url);
        Assert.Equal("task9", _handler.LastCreateConfig!.TaskId);
        Assert.Equal("cfg9", _handler.LastCreateConfig.ConfigId);

        await Client.DeleteTaskPushNotificationConfigAsync(new DeleteTaskPushNotificationConfigRequest { TaskId = "task9", Id = "cfg9" });
        Assert.Equal("cfg9", _handler.LastDeleteConfig!.Id);
    }

    [Fact]
    public async Task SendStreamingMessage_YieldsAllEvents()
    {
        _handler.StreamEvents.Add(new StreamResponse { StatusUpdate = new TaskStatusUpdateEvent { TaskId = "t", ContextId = "c", Status = new TaskStatus { State = TaskState.Working } } });
        _handler.StreamEvents.Add(new StreamResponse { Task = new AgentTask { Id = "t", ContextId = "c", Status = new TaskStatus { State = TaskState.Completed } } });

        var received = new List<StreamResponse>();
        await foreach (var streamEvent in Client.SendStreamingMessageAsync(new SendMessageRequest
        {
            Message = new Message { MessageId = "m", Role = Role.User, Parts = { Part.FromText("go") } },
        }))
        {
            received.Add(streamEvent);
        }

        Assert.Equal(2, received.Count);
        Assert.Equal(StreamResponseCase.StatusUpdate, received[0].PayloadCase);
        Assert.Equal(StreamResponseCase.Task, received[1].PayloadCase);
    }

    [Fact]
    public async Task Handler_A2AException_IsSurfacedWithErrorCode()
    {
        _handler.Error = new A2AException("no such task", A2AErrorCode.TaskNotFound);

        var exception = await Assert.ThrowsAsync<A2AException>(() =>
            Client.GetTaskAsync(new GetTaskRequest { Id = "missing" }));

        Assert.Equal(A2AErrorCode.TaskNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task Handler_A2AException_OnStream_IsSurfacedWithErrorCode()
    {
        _handler.Error = new A2AException("not cancelable", A2AErrorCode.TaskNotCancelable);

        var exception = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in Client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "t" }))
            {
                // drain
            }
        });

        Assert.Equal(A2AErrorCode.TaskNotCancelable, exception.ErrorCode);
    }
}

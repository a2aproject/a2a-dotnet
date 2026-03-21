using System.Net;
using System.Text;
using System.Text.Json;

namespace A2A.UnitTests.Client;

public class A2AHttpJsonClientTests
{
    [Fact]
    public async Task SendMessageAsync_PostsToCorrectUrlAndDeserializesResponse()
    {
        HttpRequestMessage? captured = null;
        var expected = new SendMessageResponse
        {
            Message = new Message { MessageId = "id-1", Role = Role.User, Parts = [] }
        };

        var sut = CreateClient(expected, req => captured = req);

        var result = await sut.SendMessageAsync(new SendMessageRequest
        {
            Message = new Message { Parts = [Part.FromText("Hello")], Role = Role.User, MessageId = "m-1" }
        });

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("http://localhost/message:send", captured.RequestUri!.ToString());
        Assert.Equal("application/json", captured.Content!.Headers.ContentType!.MediaType);
        Assert.NotNull(result.Message);
        Assert.Equal("id-1", result.Message.MessageId);
    }

    [Fact]
    public async Task SendStreamingMessageAsync_PostsToCorrectUrlAndYieldsEvents()
    {
        HttpRequestMessage? captured = null;
        var streamItem = new StreamResponse
        {
            Message = new Message { MessageId = "s-1", Role = Role.Agent, Parts = [Part.FromText("Hi")] }
        };

        var sut = CreateSseClient(streamItem, req => captured = req);

        var results = new List<StreamResponse>();
        await foreach (var item in sut.SendStreamingMessageAsync(new SendMessageRequest
        {
            Message = new Message { Parts = [Part.FromText("Hello")], Role = Role.User, MessageId = "m-1" }
        }))
        {
            results.Add(item);
        }

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("http://localhost/message:stream", captured.RequestUri!.ToString());
        Assert.Single(results);
        Assert.Equal("s-1", results[0].Message!.MessageId);
    }

    [Fact]
    public async Task GetTaskAsync_UsesGetWithCorrectPath()
    {
        HttpRequestMessage? captured = null;
        var expected = new AgentTask { Id = "task-1", Status = new TaskStatus { State = TaskState.Working } };

        var sut = CreateClient(expected, req => captured = req);

        var result = await sut.GetTaskAsync(new GetTaskRequest { Id = "task-1" });

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured.Method);
        Assert.Equal("http://localhost/tasks/task-1", captured.RequestUri!.ToString());
        Assert.Equal("task-1", result.Id);
    }

    [Fact]
    public async Task GetTaskAsync_IncludesHistoryLengthQueryParam()
    {
        HttpRequestMessage? captured = null;
        var expected = new AgentTask { Id = "task-1", Status = new TaskStatus { State = TaskState.Working } };

        var sut = CreateClient(expected, req => captured = req);

        await sut.GetTaskAsync(new GetTaskRequest { Id = "task-1", HistoryLength = 5 });

        Assert.NotNull(captured);
        Assert.Contains("historyLength=5", captured.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetTaskAsync_EscapesTaskId()
    {
        HttpRequestMessage? captured = null;
        var expected = new AgentTask { Id = "a/b", Status = new TaskStatus { State = TaskState.Working } };

        var sut = CreateClient(expected, req => captured = req);

        await sut.GetTaskAsync(new GetTaskRequest { Id = "a/b" });

        Assert.NotNull(captured);
        Assert.Contains("/tasks/a%2Fb", captured.RequestUri!.ToString());
    }

    [Fact]
    public async Task ListTasksAsync_UsesGetWithQueryParams()
    {
        HttpRequestMessage? captured = null;
        var expected = new ListTasksResponse { Tasks = [], NextPageToken = "", PageSize = 10, TotalSize = 0 };

        var sut = CreateClient(expected, req => captured = req);

        await sut.ListTasksAsync(new ListTasksRequest
        {
            ContextId = "ctx-1",
            Status = TaskState.Completed,
            PageSize = 10,
            PageToken = "abc",
            HistoryLength = 3
        });

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured.Method);
        var uri = captured.RequestUri!.ToString();
        Assert.Contains("contextId=ctx-1", uri);
        Assert.Contains("status=Completed", uri);
        Assert.Contains("pageSize=10", uri);
        Assert.Contains("pageToken=abc", uri);
        Assert.Contains("historyLength=3", uri);
    }

    [Fact]
    public async Task ListTasksAsync_OmitsNullQueryParams()
    {
        HttpRequestMessage? captured = null;
        var expected = new ListTasksResponse { Tasks = [], NextPageToken = "", PageSize = 10, TotalSize = 0 };

        var sut = CreateClient(expected, req => captured = req);

        await sut.ListTasksAsync(new ListTasksRequest { ContextId = "ctx-1" });

        Assert.NotNull(captured);
        var uri = captured.RequestUri!.ToString();
        Assert.Contains("contextId=ctx-1", uri);
        Assert.DoesNotContain("pageSize", uri);
        Assert.DoesNotContain("pageToken", uri);
        Assert.DoesNotContain("historyLength", uri);
    }

    [Fact]
    public async Task CancelTaskAsync_PostsToCorrectUrl()
    {
        HttpRequestMessage? captured = null;
        var expected = new AgentTask { Id = "task-1", Status = new TaskStatus { State = TaskState.Canceled } };

        var sut = CreateClient(expected, req => captured = req);

        var result = await sut.CancelTaskAsync(new CancelTaskRequest { Id = "task-1" });

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("http://localhost/tasks/task-1:cancel", captured.RequestUri!.ToString());
        Assert.Equal(TaskState.Canceled, result.Status.State);
    }

    [Fact]
    public async Task SubscribeToTaskAsync_PostsWithSse()
    {
        HttpRequestMessage? captured = null;
        var streamItem = new StreamResponse
        {
            Task = new AgentTask { Id = "task-1", Status = new TaskStatus { State = TaskState.Completed } }
        };

        var sut = CreateSseClient(streamItem, req => captured = req);

        var results = new List<StreamResponse>();
        await foreach (var item in sut.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "task-1" }))
        {
            results.Add(item);
        }

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("http://localhost/tasks/task-1:subscribe", captured.RequestUri!.ToString());
        Assert.Single(results);
    }

    [Fact]
    public async Task CreateTaskPushNotificationConfigAsync_PostsCorrectBody()
    {
        HttpRequestMessage? captured = null;
        var expected = new TaskPushNotificationConfig
        {
            Id = "cfg-1",
            TaskId = "t-1",
            PushNotificationConfig = new PushNotificationConfig { Url = "http://callback" }
        };

        var sut = CreateClient(expected, req => captured = req);

        await sut.CreateTaskPushNotificationConfigAsync(new CreateTaskPushNotificationConfigRequest
        {
            TaskId = "t-1",
            ConfigId = "cfg-1",
            Config = new PushNotificationConfig { Url = "http://callback" }
        });

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("http://localhost/tasks/t-1/pushNotificationConfigs", captured.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetTaskPushNotificationConfigAsync_UsesCorrectGetPath()
    {
        HttpRequestMessage? captured = null;
        var expected = new TaskPushNotificationConfig
        {
            Id = "cfg-1",
            TaskId = "t-1",
            PushNotificationConfig = new PushNotificationConfig { Url = "http://callback" }
        };

        var sut = CreateClient(expected, req => captured = req);

        await sut.GetTaskPushNotificationConfigAsync(new GetTaskPushNotificationConfigRequest { TaskId = "t-1", Id = "cfg-1" });

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured.Method);
        Assert.Equal("http://localhost/tasks/t-1/pushNotificationConfigs/cfg-1", captured.RequestUri!.ToString());
    }

    [Fact]
    public async Task ListTaskPushNotificationConfigAsync_UsesCorrectGetPathWithQuery()
    {
        HttpRequestMessage? captured = null;
        var expected = new ListTaskPushNotificationConfigResponse();

        var sut = CreateClient(expected, req => captured = req);

        await sut.ListTaskPushNotificationConfigAsync(new ListTaskPushNotificationConfigRequest
        {
            TaskId = "t-1",
            PageSize = 5,
            PageToken = "tok"
        });

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured.Method);
        var uri = captured.RequestUri!.ToString();
        Assert.StartsWith("http://localhost/tasks/t-1/pushNotificationConfigs", uri);
        Assert.Contains("pageSize=5", uri);
        Assert.Contains("pageToken=tok", uri);
    }

    [Fact]
    public async Task DeleteTaskPushNotificationConfigAsync_SendsDeleteRequest()
    {
        HttpRequestMessage? captured = null;
        var sut = CreateEmptyClient(HttpStatusCode.NoContent, req => captured = req);

        await sut.DeleteTaskPushNotificationConfigAsync(new DeleteTaskPushNotificationConfigRequest { TaskId = "t-1", Id = "cfg-1" });

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Delete, captured.Method);
        Assert.Equal("http://localhost/tasks/t-1/pushNotificationConfigs/cfg-1", captured.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetExtendedAgentCardAsync_UsesCorrectGetPath()
    {
        HttpRequestMessage? captured = null;
        var expected = new AgentCard { Name = "test", Description = "d", Version = "1.0" };

        var sut = CreateClient(expected, req => captured = req);

        var result = await sut.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest());

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured.Method);
        Assert.Equal("http://localhost/extendedAgentCard", captured.RequestUri!.ToString());
        Assert.Equal("test", result.Name);
    }

    [Fact]
    public async Task HttpError404_ThrowsA2AExceptionWithTaskNotFound()
    {
        var sut = CreateErrorClient(HttpStatusCode.NotFound, "Not found");

        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            sut.GetTaskAsync(new GetTaskRequest { Id = "missing" }));

        Assert.Equal(A2AErrorCode.TaskNotFound, ex.ErrorCode);
        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public async Task HttpError400_ThrowsA2AExceptionWithInvalidRequest()
    {
        var sut = CreateErrorClient(HttpStatusCode.BadRequest, "Bad request");

        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            sut.SendMessageAsync(new SendMessageRequest
            {
                Message = new Message { Parts = [], Role = Role.User, MessageId = "m" }
            }));

        Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
        Assert.Contains("400", ex.Message);
    }

    [Fact]
    public async Task HttpError500_ThrowsA2AExceptionWithInternalError()
    {
        var sut = CreateErrorClient(HttpStatusCode.InternalServerError, "Server error");

        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            sut.GetTaskAsync(new GetTaskRequest { Id = "task-1" }));

        Assert.Equal(A2AErrorCode.InternalError, ex.ErrorCode);
        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public void Constructor_ThrowsOnNullBaseUrl()
    {
        Assert.Throws<ArgumentNullException>(() => new A2AHttpJsonClient(null!));
    }

    // ---- Helpers ----

    private static A2AHttpJsonClient CreateClient<T>(T responseBody, Action<HttpRequestMessage>? onRequest = null)
    {
        var json = JsonSerializer.Serialize(responseBody, A2AJsonUtilities.DefaultOptions);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var handler = new MockHttpMessageHandler(response, onRequest);
        return new A2AHttpJsonClient(new Uri("http://localhost"), new HttpClient(handler));
    }

    private static A2AHttpJsonClient CreateSseClient<T>(T eventBody, Action<HttpRequestMessage>? onRequest = null)
    {
        var json = JsonSerializer.Serialize(eventBody, A2AJsonUtilities.DefaultOptions);
        var sse = $"event: message\ndata: {json}\n\n";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        };

        var handler = new MockHttpMessageHandler(response, onRequest);
        return new A2AHttpJsonClient(new Uri("http://localhost"), new HttpClient(handler));
    }

    private static A2AHttpJsonClient CreateEmptyClient(HttpStatusCode statusCode, Action<HttpRequestMessage>? onRequest = null)
    {
        var response = new HttpResponseMessage(statusCode);
        var handler = new MockHttpMessageHandler(response, onRequest);
        return new A2AHttpJsonClient(new Uri("http://localhost"), new HttpClient(handler));
    }

    private static A2AHttpJsonClient CreateErrorClient(HttpStatusCode statusCode, string body)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var handler = new MockHttpMessageHandler(response);
        return new A2AHttpJsonClient(new Uri("http://localhost"), new HttpClient(handler));
    }
}

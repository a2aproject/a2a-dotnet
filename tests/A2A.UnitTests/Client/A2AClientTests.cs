using System.Net;
using System.Text;
using System.Text.Json;

namespace A2A.UnitTests.Client;

public class A2AClientTests
{
    [Fact]
    public async Task SendMessageAsync_MapsRequestParamsCorrectly()
    {
        // Arrange
        string? capturedBody = null;

        var responseResult = new SendMessageResponse
        {
            Message = new Message { MessageId = "id-1", Role = Role.User, Parts = [] }
        };
        var sut = CreateA2AClient(responseResult, req => capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

        var sendRequest = new SendMessageRequest
        {
            Message = new Message
            {
                Parts = [Part.FromText("Hello")],
                Role = Role.User,
                MessageId = "msg-1",
                TaskId = "task-1",
                ContextId = "ctx-1",
                Metadata = new Dictionary<string, JsonElement> { { "foo", JsonDocument.Parse("\"bar\"").RootElement } },
                ReferenceTaskIds = ["ref-1"]
            },
            Configuration = new SendMessageConfiguration
            {
                AcceptedOutputModes = ["mode1"],
                PushNotificationConfig = new PushNotificationConfig { Url = "http://push" },
                HistoryLength = 5,
                Blocking = true
            },
            Metadata = new Dictionary<string, JsonElement> { { "baz", JsonDocument.Parse("\"qux\"").RootElement } }
        };

        // Act
        await sut.SendMessageAsync(sendRequest);

        // Assert
        Assert.NotNull(capturedBody);

        var requestJson = JsonDocument.Parse(capturedBody);
        Assert.Equal(A2AMethods.SendMessage, requestJson.RootElement.GetProperty("method").GetString());
        Assert.True(Guid.TryParse(requestJson.RootElement.GetProperty("id").GetString(), out _));

        var parameters = requestJson.RootElement.GetProperty("params").Deserialize<SendMessageRequest>(A2AJsonUtilities.DefaultOptions);
        Assert.NotNull(parameters);

        Assert.Equal(sendRequest.Message.Parts.Count, parameters.Message.Parts.Count);
        Assert.Equal(sendRequest.Message.Parts[0].Text, parameters.Message.Parts[0].Text);
        Assert.Equal(sendRequest.Message.Role, parameters.Message.Role);
        Assert.Equal(sendRequest.Message.MessageId, parameters.Message.MessageId);
    }

    [Fact]
    public async Task SendMessageAsync_MapsResponseCorrectly()
    {
        // Arrange
        var expectedResponse = new SendMessageResponse
        {
            Message = new Message
            {
                Role = Role.Agent,
                Parts = [Part.FromText("Test text")],
                MessageId = "msg-123",
                TaskId = "task-456",
                ContextId = "ctx-789"
            }
        };

        var sut = CreateA2AClient(expectedResponse);

        var sendRequest = new SendMessageRequest { Message = new Message { Parts = [], Role = Role.User } };

        // Act
        var result = await sut.SendMessageAsync(sendRequest);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Message);
        Assert.Equal(expectedResponse.Message.Role, result.Message!.Role);
        Assert.Single(result.Message.Parts);
        Assert.Equal("Test text", result.Message.Parts[0].Text);
        Assert.Equal(expectedResponse.Message.MessageId, result.Message.MessageId);
    }

    [Fact]
    public async Task GetTaskAsync_MapsRequestParamsCorrectly()
    {
        // Arrange
        string? capturedBody = null;

        var sut = CreateA2AClient(new AgentTask { Id = "id-1", ContextId = "ctx-1" }, req => capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

        var request = new GetTaskRequest { Id = "task-1" };

        // Act
        await sut.GetTaskAsync(request);

        // Assert
        Assert.NotNull(capturedBody);

        var requestJson = JsonDocument.Parse(capturedBody);
        Assert.Equal(A2AMethods.GetTask, requestJson.RootElement.GetProperty("method").GetString());

        var parameters = requestJson.RootElement.GetProperty("params").Deserialize<GetTaskRequest>(A2AJsonUtilities.DefaultOptions);
        Assert.NotNull(parameters);
        Assert.Equal("task-1", parameters.Id);
    }

    [Fact]
    public async Task GetTaskAsync_MapsResponseCorrectly()
    {
        // Arrange
        var expectedTask = new AgentTask
        {
            Id = "task-1",
            ContextId = "ctx-ctx",
            Status = new TaskStatus { State = TaskState.Working },
            Artifacts = [new Artifact { ArtifactId = "a1", Parts = { Part.FromText("part") } }],
            History = [new Message { MessageId = "m1" }],
            Metadata = new Dictionary<string, JsonElement> { { "foo", JsonDocument.Parse("\"bar\"").RootElement } }
        };

        var sut = CreateA2AClient(expectedTask);

        // Act
        var result = await sut.GetTaskAsync(new GetTaskRequest { Id = "task-1" });

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedTask.Id, result.Id);
        Assert.Equal(expectedTask.ContextId, result.ContextId);
        Assert.Equal(expectedTask.Status.State, result.Status.State);
        Assert.Equal(expectedTask.Artifacts![0].ArtifactId, result.Artifacts![0].ArtifactId);
    }

    [Fact]
    public async Task CancelTaskAsync_MapsRequestParamsCorrectly()
    {
        // Arrange
        string? capturedBody = null;

        var sut = CreateA2AClient(new AgentTask { Id = "task-2" }, req => capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

        var cancelRequest = new CancelTaskRequest
        {
            Id = "task-2",
        };

        // Act
        await sut.CancelTaskAsync(cancelRequest);

        // Assert
        Assert.NotNull(capturedBody);

        var requestJson = JsonDocument.Parse(capturedBody);
        Assert.Equal(A2AMethods.CancelTask, requestJson.RootElement.GetProperty("method").GetString());

        var parameters = requestJson.RootElement.GetProperty("params").Deserialize<CancelTaskRequest>(A2AJsonUtilities.DefaultOptions);
        Assert.NotNull(parameters);
        Assert.Equal(cancelRequest.Id, parameters.Id);
    }

    [Fact]
    public async Task SendStreamingMessageAsync_MapsResponseCorrectly()
    {
        // Arrange
        var expectedResponse = new StreamResponse
        {
            Message = new Message
            {
                Role = Role.Agent,
                Parts = [Part.FromText("Test text")],
                MessageId = "msg-123",
            }
        };

        var sut = CreateA2AClient(expectedResponse, isSse: true);

        var sendRequest = new SendMessageRequest { Message = new Message { Parts = [], Role = Role.User } };

        // Act
        StreamResponse? result = null;
        await foreach (var item in sut.SendStreamingMessageAsync(sendRequest))
        {
            result = item;
            break;
        }

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Message);
        Assert.Equal(expectedResponse.Message.Role, result.Message!.Role);
        Assert.Single(result.Message.Parts);
        Assert.Equal("Test text", result.Message.Parts[0].Text);
    }

    [Fact]
    public async Task SubscribeToTaskAsync_MapsRequestParamsCorrectly()
    {
        // Arrange
        string? capturedBody = null;

        var responseResult = new StreamResponse
        {
            Message = new Message { MessageId = "id-1", Role = Role.User, Parts = [] }
        };
        var sut = CreateA2AClient(responseResult, req => capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult(), isSse: true);

        var request = new SubscribeToTaskRequest { Id = "task-123" };

        // Act
        await foreach (var _ in sut.SubscribeToTaskAsync(request))
        {
            break;
        }

        // Assert
        Assert.NotNull(capturedBody);

        var requestJson = JsonDocument.Parse(capturedBody);
        Assert.Equal(A2AMethods.SubscribeToTask, requestJson.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public async Task SendStreamingMessageAsync_ThrowsOnJsonRpcError()
    {
        // Arrange
        var sut = CreateA2AClient(JsonRpcResponse.InvalidParamsResponse("test-id"), isSse: true);

        var sendRequest = new SendMessageRequest { Message = new Message { Parts = [], Role = Role.User } };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in sut.SendStreamingMessageAsync(sendRequest))
            {
            }
        });

        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
    }

    [Fact]
    public async Task SendMessageAsync_ThrowsOnJsonRpcError()
    {
        // Arrange
        var sut = CreateA2AClient(JsonRpcResponse.MethodNotFoundResponse("test-id"));

        var sendRequest = new SendMessageRequest { Message = new Message { Parts = [], Role = Role.User } };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await sut.SendMessageAsync(sendRequest);
        });

        Assert.Equal(A2AErrorCode.MethodNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task ListTasksAsync_SendsCorrectMethodAndParams()
    {
        // Arrange
        string? capturedBody = null;

        var sut = CreateA2AClient(
            new ListTasksResponse { Tasks = [] },
            req => capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

        var request = new ListTasksRequest { ContextId = "ctx-1" };

        // Act
        await sut.ListTasksAsync(request);

        // Assert
        Assert.NotNull(capturedBody);

        var requestJson = JsonDocument.Parse(capturedBody);
        Assert.Equal(A2AMethods.ListTasks, requestJson.RootElement.GetProperty("method").GetString());

        var parameters = requestJson.RootElement.GetProperty("params").Deserialize<ListTasksRequest>(A2AJsonUtilities.DefaultOptions);
        Assert.NotNull(parameters);
        Assert.Equal("ctx-1", parameters.ContextId);
    }

    [Fact]
    public async Task CancelTaskAsync_SendsCorrectMethod()
    {
        // Arrange
        string? capturedBody = null;

        var sut = CreateA2AClient(
            new AgentTask { Id = "task-1" },
            req => capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

        // Act
        await sut.CancelTaskAsync(new CancelTaskRequest { Id = "task-1" });

        // Assert
        Assert.NotNull(capturedBody);

        var requestJson = JsonDocument.Parse(capturedBody);
        Assert.Equal(A2AMethods.CancelTask, requestJson.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public async Task GetExtendedAgentCardAsync_SendsCorrectMethod()
    {
        // Arrange
        string? capturedBody = null;

        var sut = CreateA2AClient(
            new AgentCard { Name = "Agent", Description = "Desc", SupportedInterfaces = [new AgentInterface { Url = "http://test", ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" }] },
            req => capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

        // Act
        await sut.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest());

        // Assert
        Assert.NotNull(capturedBody);

        var requestJson = JsonDocument.Parse(capturedBody);
        Assert.Equal(A2AMethods.GetExtendedAgentCard, requestJson.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public async Task CreatePushNotificationConfigAsync_SendsCorrectMethod()
    {
        // Arrange
        string? capturedBody = null;

        var sut = CreateA2AClient(
            new TaskPushNotificationConfig { Id = "cfg-1", TaskId = "t-1", PushNotificationConfig = new PushNotificationConfig { Url = "http://push" } },
            req => capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

        // Act
        await sut.CreateTaskPushNotificationConfigAsync(new CreateTaskPushNotificationConfigRequest { TaskId = "t-1", ConfigId = "cfg-1", Config = new PushNotificationConfig { Url = "http://push" } });

        // Assert
        Assert.NotNull(capturedBody);

        var requestJson = JsonDocument.Parse(capturedBody);
        Assert.Equal(A2AMethods.CreateTaskPushNotificationConfig, requestJson.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public async Task DeletePushNotificationConfigAsync_SendsCorrectMethod()
    {
        // Arrange
        string? capturedBody = null;

        var sut = CreateA2AClient(
            new object(),
            req => capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

        // Act
        await sut.DeleteTaskPushNotificationConfigAsync(new DeleteTaskPushNotificationConfigRequest { Id = "cfg-1", TaskId = "t-1" });

        // Assert
        Assert.NotNull(capturedBody);

        var requestJson = JsonDocument.Parse(capturedBody);
        Assert.Equal(A2AMethods.DeleteTaskPushNotificationConfig, requestJson.RootElement.GetProperty("method").GetString());
    }

    private static A2AClient CreateA2AClient(object result, Action<HttpRequestMessage>? onRequest = null, bool isSse = false)
    {
        var response = new JsonRpcResponse
        {
            Id = "test-id",
            Result = JsonSerializer.SerializeToNode(result, A2AJsonUtilities.DefaultOptions)
        };

        return CreateA2AClient(response, onRequest, isSse);
    }

    private static A2AClient CreateA2AClient(JsonRpcResponse jsonResponse, Action<HttpRequestMessage>? onRequest = null, bool isSse = false)
    {
        var responseContent = JsonSerializer.Serialize(jsonResponse, A2AJsonUtilities.DefaultOptions);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                isSse ? $"event: message\ndata: {responseContent}\n\n" : responseContent,
                Encoding.UTF8,
                isSse ? "text/event-stream" : "application/json")
        };

        var handler = new MockHttpMessageHandler(response, onRequest);
        var httpClient = new HttpClient(handler);

        return new A2AClient(new Uri("http://localhost"), httpClient);
    }
}

using System.Net;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;

namespace A2A.UnitTests.Client;

public class A2AClientTests
{
    [Fact]
    public async Task SendMessageAsync_MapsRequestParamsCorrectly()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;

        var sut = CreateA2AClient(new A2AResponse(), req => capturedRequest = req);

        var sendParams = new MessageSendParams
        {
            Message = new Message
            {
                Parts = [new TextPart { Text = "Hello" }],
                Role = MessageRole.User,
                MessageId = "msg-1",
                TaskId = "task-1",
                ContextId = "ctx-1",
                Metadata = new Dictionary<string, JsonElement> { { "foo", JsonDocument.Parse("\"bar\"").RootElement } },
                ReferenceTaskIds = ["ref-1"]
            },
            Configuration = new MessageSendConfiguration
            {
                AcceptedOutputModes = ["mode1"],
                PushNotification = new PushNotificationConfig { Url = "http://push" },
                HistoryLength = 5,
                Blocking = true
            },
            Metadata = new Dictionary<string, JsonElement> { { "baz", JsonDocument.Parse("\"qux\"").RootElement } }
        };

        // Act
        await sut.SendMessageAsync(sendParams);

        // Assert
        Assert.NotNull(capturedRequest);

        var requestJson = JsonDocument.Parse(await capturedRequest.Content!.ReadAsStringAsync());
        Assert.Equal("message/send", requestJson.RootElement.GetProperty("method").GetString());
        Assert.True(Guid.TryParse(requestJson.RootElement.GetProperty("id").GetString(), out _));

        var parameters = requestJson.RootElement.GetProperty("params").Deserialize<MessageSendParams>();
        Assert.NotNull(parameters);

        Assert.Equal(sendParams.Message.Parts.Count, parameters.Message.Parts.Count);
        Assert.Equal(((TextPart)sendParams.Message.Parts[0]).Text, ((TextPart)parameters.Message.Parts[0]).Text);
        Assert.Equal(sendParams.Message.Role, parameters.Message.Role);
        Assert.Equal(sendParams.Message.MessageId, parameters.Message.MessageId);
        Assert.Equal(sendParams.Message.TaskId, parameters.Message.TaskId);
        Assert.Equal(sendParams.Message.ContextId, parameters.Message.ContextId);
        Assert.Equal(sendParams.Message.Metadata["foo"].GetString(), parameters.Message.Metadata!["foo"].GetString());
        Assert.Equal(sendParams.Message.ReferenceTaskIds[0], parameters.Message.ReferenceTaskIds![0]);

        Assert.NotNull(parameters.Configuration);
        Assert.Equal(sendParams.Configuration.AcceptedOutputModes[0], parameters.Configuration.AcceptedOutputModes![0]);
        Assert.Equal(sendParams.Configuration.PushNotification.Url, parameters.Configuration.PushNotification!.Url);
        Assert.Equal(sendParams.Configuration.HistoryLength, parameters.Configuration.HistoryLength);
        Assert.Equal(sendParams.Configuration.Blocking, parameters.Configuration.Blocking);

        Assert.Equal(sendParams.Metadata["baz"].GetString(), parameters.Metadata!["baz"].GetString());
    }

    [Fact]
    public async Task SendMessageAsync_MapsResponseCorrectly()
    {
        // Arrange
        var expectedMessage = new Message
        {
            Role = MessageRole.Agent,
            Parts =
            [
                new TextPart { Text = "Test text" },
                new DataPart { Data = new Dictionary<string, JsonElement> { { "key", JsonDocument.Parse("\"value\"").RootElement } } },
            ],
            Metadata = new Dictionary<string, JsonElement> { { "metaKey", JsonDocument.Parse("\"metaValue\"").RootElement } },
            ReferenceTaskIds = ["ref1", "ref2"],
            MessageId = "msg-123",
            TaskId = "task-456",
            ContextId = "ctx-789"
        };
        var sut = CreateA2AClient<A2AResponse>(expectedMessage);
        var sendParams = new MessageSendParams();

        // Act
        var result = await sut.SendMessageAsync(sendParams) as Message;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedMessage.Role, result.Role);
        Assert.Equal(expectedMessage.Parts.Count, result.Parts.Count);
        Assert.IsType<TextPart>(result.Parts[0]);
        Assert.Equal(((TextPart)expectedMessage.Parts[0]).Text, ((TextPart)result.Parts[0]).Text);
        Assert.IsType<DataPart>(result.Parts[1]);
        Assert.Equal(((DataPart)expectedMessage.Parts[1]).Data["key"].GetString(), ((DataPart)result.Parts[1]).Data["key"].GetString());
        Assert.Equal(expectedMessage.Metadata["metaKey"].GetString(), result.Metadata!["metaKey"].GetString());
        Assert.Equal(expectedMessage.ReferenceTaskIds, result.ReferenceTaskIds);
        Assert.Equal(expectedMessage.MessageId, result.MessageId);
        Assert.Equal(expectedMessage.TaskId, result.TaskId);
        Assert.Equal(expectedMessage.ContextId, result.ContextId);
    }

    [Fact]
    public async Task GetTaskAsync_MapsRequestParamsCorrectly()
    {
        // Arrange
        var expectedResponse = new AgentTask { Id = "task-1", ContextId = "ctx-1" };

        HttpRequestMessage? capturedRequest = null;

        var sut = CreateA2AClient(expectedResponse, req => capturedRequest = req);

        var taskId = "task-1";

        // Act
        await sut.GetTaskAsync(taskId);

        // Assert
        Assert.NotNull(capturedRequest);
        var requestJson = JsonDocument.Parse(await capturedRequest.Content!.ReadAsStringAsync());
        Assert.Equal("tasks/get", requestJson.RootElement.GetProperty("method").GetString());
        Assert.True(Guid.TryParse(requestJson.RootElement.GetProperty("id").GetString(), out _));
        var parameters = requestJson.RootElement.GetProperty("params").Deserialize<TaskIdParams>();
        Assert.NotNull(parameters);
        Assert.Equal(taskId, parameters.Id);
    }

    [Fact]
    public async Task GetTaskAsync_MapsResponseCorrectly()
    {
        // Arrange
        var expectedTask = new AgentTask
        {
            Id = "task-1",
            ContextId = "ctx-ctx",
            Status = new AgentTaskStatus { State = TaskState.Working },
            Artifacts = [new Artifact { ArtifactId = "a1", Parts = { new TextPart { Text = "part" } } }],
            History = [new Message { MessageId = "m1" }],
            Metadata = new Dictionary<string, JsonElement> { { "foo", JsonDocument.Parse("\"bar\"").RootElement } }
        };
        var sut = CreateA2AClient(expectedTask);

        // Act
        var result = await sut.GetTaskAsync("task-1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedTask.Id, result.Id);
        Assert.Equal(expectedTask.ContextId, result.ContextId);
        Assert.Equal(expectedTask.Status.State, result.Status.State);
        Assert.Equal(expectedTask.Artifacts![0].ArtifactId, result.Artifacts![0].ArtifactId);
        Assert.Equal(((TextPart)expectedTask.Artifacts![0].Parts[0]).Text, ((TextPart)result.Artifacts![0].Parts[0]).Text);
        Assert.Equal(expectedTask.History![0].MessageId, result.History![0].MessageId);
        Assert.Equal(expectedTask.Metadata!["foo"].GetString(), result.Metadata!["foo"].GetString());
    }

    [Fact]
    public async Task CancelTaskAsync_MapsRequestParamsCorrectly()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;

        var sut = CreateA2AClient(new AgentTask { Id = "task-2" }, req => capturedRequest = req);

        var taskIdParams = new TaskIdParams
        {
            Id = "task-2",
            Metadata = new Dictionary<string, JsonElement> { { "meta", JsonDocument.Parse("\"val\"").RootElement } }
        };

        // Act
        await sut.CancelTaskAsync(taskIdParams);

        // Assert
        Assert.NotNull(capturedRequest);
        var requestJson = JsonDocument.Parse(await capturedRequest.Content!.ReadAsStringAsync());
        Assert.Equal("tasks/cancel", requestJson.RootElement.GetProperty("method").GetString());
        Assert.True(Guid.TryParse(requestJson.RootElement.GetProperty("id").GetString(), out _));
        var parameters = requestJson.RootElement.GetProperty("params").Deserialize<TaskIdParams>();
        Assert.NotNull(parameters);
        Assert.Equal(taskIdParams.Id, parameters.Id);
        Assert.Equal(taskIdParams.Metadata!["meta"].GetString(), parameters.Metadata!["meta"].GetString());
    }

    [Fact]
    public async Task CancelTaskAsync_MapsResponseCorrectly()
    {
        // Arrange
        var expectedTask = new AgentTask
        {
            Id = "task-1",
            ContextId = "ctx-ctx",
            Status = new AgentTaskStatus { State = TaskState.Working },
            Artifacts = [new Artifact { ArtifactId = "a1", Parts = { new TextPart { Text = "part" } } }],
            History = [new Message { MessageId = "m1" }],
            Metadata = new Dictionary<string, JsonElement> { { "foo", JsonDocument.Parse("\"bar\"").RootElement } }
        };
        var sut = CreateA2AClient(expectedTask);
        var taskIdParams = new TaskIdParams { Id = "task-2" };

        // Act
        var result = await sut.CancelTaskAsync(taskIdParams);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedTask.Id, result.Id);
        Assert.Equal(expectedTask.ContextId, result.ContextId);
        Assert.Equal(expectedTask.Status.State, result.Status.State);
        Assert.Equal(expectedTask.Artifacts![0].ArtifactId, result.Artifacts![0].ArtifactId);
        Assert.Equal(((TextPart)expectedTask.Artifacts![0].Parts[0]).Text, ((TextPart)result.Artifacts![0].Parts[0]).Text);
        Assert.Equal(expectedTask.History![0].MessageId, result.History![0].MessageId);
        Assert.Equal(expectedTask.Metadata!["foo"].GetString(), result.Metadata!["foo"].GetString());
    }

    [Fact]
    public async Task SetPushNotificationAsync_MapsRequestParamsCorrectly()
    {
        // Arrange
        var pushConfig = new TaskPushNotificationConfig
        {
            TaskId = "task-3",
            PushNotificationConfig = new PushNotificationConfig
            {
                Url = "http://push-url",
                Token = "tok",
                Authentication = new AuthenticationInfo
                {
                    Schemes = ["Bearer"],
                }
            }
        };

        HttpRequestMessage? capturedRequest = null;

        var sut = CreateA2AClient(pushConfig, req => capturedRequest = req);

        // Act
        await sut.SetPushNotificationAsync(pushConfig);

        // Assert
        Assert.NotNull(capturedRequest);
        var requestJson = JsonDocument.Parse(await capturedRequest.Content!.ReadAsStringAsync());
        Assert.Equal("task/pushNotification/set", requestJson.RootElement.GetProperty("method").GetString());
        Assert.True(Guid.TryParse(requestJson.RootElement.GetProperty("id").GetString(), out _));
        var parameters = requestJson.RootElement.GetProperty("params").Deserialize<TaskPushNotificationConfig>();
        Assert.NotNull(parameters);
        Assert.Equal(pushConfig.TaskId, parameters.TaskId);
        Assert.Equal(pushConfig.PushNotificationConfig.Url, parameters.PushNotificationConfig.Url);
        Assert.Equal(pushConfig.PushNotificationConfig.Token, parameters.PushNotificationConfig.Token);
        Assert.Equal(pushConfig.PushNotificationConfig.Authentication!.Schemes, parameters.PushNotificationConfig.Authentication!.Schemes);
    }

    [Fact]
    public async Task SetPushNotificationAsync_MapsResponseCorrectly()
    {
        // Arrange
        var expectedConfig = new TaskPushNotificationConfig
        {
            TaskId = "task-3",
            PushNotificationConfig = new PushNotificationConfig
            {
                Url = "http://push-url",
                Token = "tok",
                Authentication = new AuthenticationInfo
                {
                    Schemes = ["Bearer"],
                }
            }
        };
        var sut = CreateA2AClient(expectedConfig);

        // Act
        var result = await sut.SetPushNotificationAsync(expectedConfig);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedConfig.TaskId, result.TaskId);
        Assert.Equal(expectedConfig.PushNotificationConfig.Url, result.PushNotificationConfig.Url);
        Assert.Equal(expectedConfig.PushNotificationConfig.Token, result.PushNotificationConfig.Token);
        Assert.Equal(expectedConfig.PushNotificationConfig.Authentication!.Schemes, result.PushNotificationConfig.Authentication!.Schemes);
    }

    [Fact]
    public async Task GetPushNotificationAsync_MapsRequestParamsCorrectly()
    {
        // Arrange
        var config = new TaskPushNotificationConfig
        {
            TaskId = "task-4",
            PushNotificationConfig = new PushNotificationConfig { Url = "http://push-url2" }
        };

        HttpRequestMessage? capturedRequest = null;

        var sut = CreateA2AClient(config, req => capturedRequest = req);

        var taskIdParams = new TaskIdParams
        {
            Id = "task-4",
            Metadata = new Dictionary<string, JsonElement> { { "meta", JsonDocument.Parse("\"val\"").RootElement } }
        };

        // Act
        await sut.GetPushNotificationAsync(taskIdParams);

        // Assert
        Assert.NotNull(capturedRequest);
        var requestJson = JsonDocument.Parse(await capturedRequest.Content!.ReadAsStringAsync());
        Assert.Equal("task/pushNotification/get", requestJson.RootElement.GetProperty("method").GetString());
        Assert.True(Guid.TryParse(requestJson.RootElement.GetProperty("id").GetString(), out _));
        var parameters = requestJson.RootElement.GetProperty("params").Deserialize<TaskIdParams>();
        Assert.NotNull(parameters);
        Assert.Equal(taskIdParams.Id, parameters.Id);
        Assert.Equal(taskIdParams.Metadata!["meta"].GetString(), parameters.Metadata!["meta"].GetString());
    }

    [Fact]
    public async Task GetPushNotificationAsync_MapsResponseCorrectly()
    {
        // Arrange
        var expectedConfig = new TaskPushNotificationConfig
        {
            TaskId = "task-4",
            PushNotificationConfig = new PushNotificationConfig
            {
                Url = "http://push-url2",
                Token = "tok2",
                Authentication = new AuthenticationInfo
                {
                    Schemes = ["Bearer"]
                }
            }
        };
        var sut = CreateA2AClient(expectedConfig);
        var taskIdParams = new TaskIdParams { Id = "task-4" };

        // Act
        var result = await sut.GetPushNotificationAsync(taskIdParams);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedConfig.TaskId, result.TaskId);
        Assert.Equal(expectedConfig.PushNotificationConfig.Url, result.PushNotificationConfig.Url);
        Assert.Equal(expectedConfig.PushNotificationConfig.Token, result.PushNotificationConfig.Token);
        Assert.Equal(expectedConfig.PushNotificationConfig.Authentication!.Schemes, result.PushNotificationConfig.Authentication!.Schemes);
    }

    [Fact]
    public async Task SendMessageStreamAsync_MapsRequestParamsCorrectly()
    {
        // Arrange
        var sendParams = new MessageSendParams
        {
            Message = new Message
            {
                Parts = [new TextPart { Text = "Hello" }],
                Role = MessageRole.User,
                MessageId = "msg-1",
                TaskId = "task-1",
                ContextId = "ctx-1",
                Metadata = new Dictionary<string, JsonElement> { { "foo", JsonDocument.Parse("\"bar\"").RootElement } },
                ReferenceTaskIds = ["ref-1"]
            },
            Configuration = new MessageSendConfiguration
            {
                AcceptedOutputModes = ["mode1"],
                PushNotification = new PushNotificationConfig { Url = "http://push" },
                HistoryLength = 5,
                Blocking = true
            },
            Metadata = new Dictionary<string, JsonElement> { { "baz", JsonDocument.Parse("\"qux\"").RootElement } }
        };

        HttpRequestMessage? capturedRequest = null;
        // Simulate a minimal valid SSE response
        var jsonRpcResponse = JsonSerializer.Serialize(new JsonRpcResponse
        {
            Id = "test-id",
            Result = JsonSerializer.SerializeToNode(new { })
        });
        var sseStream = new MemoryStream(Encoding.UTF8.GetBytes($"event: message\ndata: {jsonRpcResponse}\n\n"));
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(sseStream)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");

        var sut = CreateA2AClient(response, req => capturedRequest = req);

        // Act
        await foreach (var _ in sut.SendMessageStreamAsync(sendParams))
        {
            break; // Only need to trigger the request
        }

        // Assert
        Assert.NotNull(capturedRequest);
        var requestJson = JsonDocument.Parse(await capturedRequest.Content!.ReadAsStringAsync());
        Assert.Equal("message/stream", requestJson.RootElement.GetProperty("method").GetString());
        Assert.True(Guid.TryParse(requestJson.RootElement.GetProperty("id").GetString(), out _));
        var parameters = requestJson.RootElement.GetProperty("params").Deserialize<MessageSendParams>();
        Assert.NotNull(parameters);
        Assert.Equal(sendParams.Message.Parts.Count, parameters.Message.Parts.Count);
        Assert.Equal(((TextPart)sendParams.Message.Parts[0]).Text, ((TextPart)parameters.Message.Parts[0]).Text);
        Assert.Equal(sendParams.Message.Role, parameters.Message.Role);
        Assert.Equal(sendParams.Message.MessageId, parameters.Message.MessageId);
        Assert.Equal(sendParams.Message.TaskId, parameters.Message.TaskId);
        Assert.Equal(sendParams.Message.ContextId, parameters.Message.ContextId);
        Assert.Equal(sendParams.Message.Metadata["foo"].GetString(), parameters.Message.Metadata!["foo"].GetString());
        Assert.Equal(sendParams.Message.ReferenceTaskIds[0], parameters.Message.ReferenceTaskIds![0]);
        Assert.NotNull(parameters.Configuration);
        Assert.Equal(sendParams.Configuration.AcceptedOutputModes[0], parameters.Configuration.AcceptedOutputModes![0]);
        Assert.Equal(sendParams.Configuration.PushNotification.Url, parameters.Configuration.PushNotification!.Url);
        Assert.Equal(sendParams.Configuration.HistoryLength, parameters.Configuration.HistoryLength);
        Assert.Equal(sendParams.Configuration.Blocking, parameters.Configuration.Blocking);
        Assert.Equal(sendParams.Metadata["baz"].GetString(), parameters.Metadata!["baz"].GetString());
    }

    [Fact]
    public async Task SendMessageStreamAsync_MapsResponseCorrectly()
    {
        // Arrange
        var expectedMessage = new Message
        {
            Role = MessageRole.Agent,
            Parts =
            [
                new TextPart { Text = "Test text" },
                new DataPart { Data = new Dictionary<string, JsonElement> { { "key", JsonDocument.Parse("\"value\"").RootElement } } },
            ],
            Metadata = new Dictionary<string, JsonElement> { { "metaKey", JsonDocument.Parse("\"metaValue\"").RootElement } },
            ReferenceTaskIds = ["ref1", "ref2"],
            MessageId = "msg-123",
            TaskId = "task-456",
            ContextId = "ctx-789"
        };
        var jsonRpcResponse = JsonSerializer.Serialize(new JsonRpcResponse
        {
            Id = "test-id",
            Result = JsonSerializer.SerializeToNode<A2AEvent>(expectedMessage, A2AJsonUtilities.DefaultOptions)
        });
        var sseStream = new MemoryStream(Encoding.UTF8.GetBytes($"event: message\ndata: {jsonRpcResponse}\n\n"));
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(sseStream)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        var sut = CreateA2AClient(response);
        var sendParams = new MessageSendParams();

        // Act
        SseItem<A2AEvent>? result = null;
        await foreach (var item in sut.SendMessageStreamAsync(sendParams))
        {
            result = item;
            break;
        }

        // Assert
        Assert.NotNull(result);
        var message = Assert.IsType<Message>(result.Value.Data);
        Assert.Equal(expectedMessage.Role, message.Role);
        Assert.Equal(expectedMessage.Parts.Count, message.Parts.Count);
        Assert.IsType<TextPart>(message.Parts[0]);
        Assert.Equal(((TextPart)expectedMessage.Parts[0]).Text, ((TextPart)message.Parts[0]).Text);
        Assert.IsType<DataPart>(message.Parts[1]);
        Assert.Equal(((DataPart)expectedMessage.Parts[1]).Data["key"].GetString(), ((DataPart)message.Parts[1]).Data["key"].GetString());
        Assert.Equal(expectedMessage.Metadata["metaKey"].GetString(), message.Metadata!["metaKey"].GetString());
        Assert.Equal(expectedMessage.ReferenceTaskIds, message.ReferenceTaskIds);
        Assert.Equal(expectedMessage.MessageId, message.MessageId);
        Assert.Equal(expectedMessage.TaskId, message.TaskId);
        Assert.Equal(expectedMessage.ContextId, message.ContextId);
    }

    [Fact]
    public async Task ResubscribeToTaskAsync_MapsRequestParamsCorrectly()
    {
        // Arrange
        var taskId = "task-123";
        HttpRequestMessage? capturedRequest = null;
        // Simulate a minimal valid SSE response
        var jsonRpcResponse = JsonSerializer.Serialize(new JsonRpcResponse
        {
            Id = "test-id",
            Result = JsonSerializer.SerializeToNode(new { })
        });
        var sseStream = new MemoryStream(Encoding.UTF8.GetBytes($"event: message\ndata: {jsonRpcResponse}\n\n"));
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(sseStream)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        var sut = CreateA2AClient(response, req => capturedRequest = req);

        // Act
        await foreach (var _ in sut.ResubscribeToTaskAsync(taskId))
        {
            break; // Only need to trigger the request
        }

        // Assert
        Assert.NotNull(capturedRequest);
        var requestJson = JsonDocument.Parse(await capturedRequest.Content!.ReadAsStringAsync());
        Assert.Equal("tasks/resubscribe", requestJson.RootElement.GetProperty("method").GetString());
        Assert.True(Guid.TryParse(requestJson.RootElement.GetProperty("id").GetString(), out _));
        var parameters = requestJson.RootElement.GetProperty("params").Deserialize<TaskIdParams>();
        Assert.NotNull(parameters);
        Assert.Equal(taskId, parameters.Id);
    }

    [Fact]
    public async Task ResubscribeToTaskAsync_MapsResponseCorrectly()
    {
        // Arrange
        var expectedMessage = new Message
        {
            Role = MessageRole.Agent,
            Parts =
            [
                new TextPart { Text = "Test text" },
                new DataPart { Data = new Dictionary<string, JsonElement> { { "key", JsonDocument.Parse("\"value\"").RootElement } } },
            ],
            Metadata = new Dictionary<string, JsonElement> { { "metaKey", JsonDocument.Parse("\"metaValue\"").RootElement } },
            ReferenceTaskIds = ["ref1", "ref2"],
            MessageId = "msg-123",
            TaskId = "task-456",
            ContextId = "ctx-789"
        };
        var jsonRpcResponse = JsonSerializer.Serialize(new JsonRpcResponse
        {
            Id = "test-id",
            Result = JsonSerializer.SerializeToNode<A2AEvent>(expectedMessage, A2AJsonUtilities.DefaultOptions)
        });
        var sseStream = new MemoryStream(Encoding.UTF8.GetBytes($"event: message\ndata: {jsonRpcResponse}\n\n"));
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(sseStream)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        var sut = CreateA2AClient(response);

        // Act
        SseItem<A2AEvent>? result = null;
        await foreach (var item in sut.ResubscribeToTaskAsync("task-123"))
        {
            result = item;
            break;
        }

        // Assert
        Assert.NotNull(result);
        var message = Assert.IsType<Message>(result.Value.Data);
        Assert.Equal(expectedMessage.Role, message.Role);
        Assert.Equal(expectedMessage.Parts.Count, message.Parts.Count);
        Assert.IsType<TextPart>(message.Parts[0]);
        Assert.Equal(((TextPart)expectedMessage.Parts[0]).Text, ((TextPart)message.Parts[0]).Text);
        Assert.IsType<DataPart>(message.Parts[1]);
        Assert.Equal(((DataPart)expectedMessage.Parts[1]).Data["key"].GetString(), ((DataPart)message.Parts[1]).Data["key"].GetString());
        Assert.Equal(expectedMessage.Metadata["metaKey"].GetString(), message.Metadata!["metaKey"].GetString());
        Assert.Equal(expectedMessage.ReferenceTaskIds, message.ReferenceTaskIds);
        Assert.Equal(expectedMessage.MessageId, message.MessageId);
        Assert.Equal(expectedMessage.TaskId, message.TaskId);
        Assert.Equal(expectedMessage.ContextId, message.ContextId);
    }

    [Fact]
    public async Task SendMessageStreamAsync_ThrowsOnJsonRpcError()
    {
        // Arrange
        var jsonRpcErrorResponse = JsonSerializer.Serialize(JsonRpcResponse.InvalidParamsResponse("test-id"));
        var sseStream = new MemoryStream(Encoding.UTF8.GetBytes($"event: message\ndata: {jsonRpcErrorResponse}\n\n"));
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(sseStream)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        var sut = CreateA2AClient(response);
        var sendParams = new MessageSendParams();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in sut.SendMessageStreamAsync(sendParams))
            {
                // Should throw before yielding any items
            }
        });

        Assert.Contains("-32602", exception.Message);
        Assert.Contains("Invalid parameters", exception.Message);
    }

    [Fact]
    public async Task SendMessageAsync_ThrowsOnJsonRpcError()
    {
        // Arrange
        var jsonRpcErrorResponse = JsonSerializer.Serialize(JsonRpcResponse.MethodNotFoundResponse("test-id"));
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonRpcErrorResponse, Encoding.UTF8, "application/json")
        };
        var sut = CreateA2AClient(response);
        var sendParams = new MessageSendParams();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await sut.SendMessageAsync(sendParams);
        });

        Assert.Contains("-32601", exception.Message);
        Assert.Contains("Method not found", exception.Message);
    }

    private static A2AClient CreateA2AClient<T>(T result, Action<HttpRequestMessage>? onRequest = null)
    {
        var jsonResponse = JsonSerializer.Serialize(new JsonRpcResponse
        {
            Id = "1",
            Result = JsonSerializer.SerializeToNode(result)
        });

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        return CreateA2AClient(response, onRequest);
    }

    private static A2AClient CreateA2AClient(HttpResponseMessage response, Action<HttpRequestMessage>? onRequest = null)
    {
        var handler = new MockHttpMessageHandler(response, onRequest);

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        return new A2AClient(httpClient);
    }
}

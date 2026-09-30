using System.Net;
using System.Text;
using System.Text.Json;

namespace A2A.UnitTests.Client;

public sealed class A2AStandardClientOperationTests
{
    [Fact]
    public async Task JsonRpcConvenienceMethods_UseStandardOperationBindings()
    {
        var requests = new List<CapturedRequest>();
        var client = new A2AClient(
            new Uri("http://localhost/a2a"),
            CreateStandardBindings(),
            new HttpClient(new RecordingHandler(request =>
            {
                var body = request.Content!.ReadAsStringAsync()
                    .GetAwaiter()
                    .GetResult();
                requests.Add(CapturedRequest.Create(request, body));
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                var method = root.GetProperty("method").GetString();
                object? result = method switch
                {
                    A2AMethods.SendMessage => new SendMessageResponse
                    {
                        Message = CreateMessage("send-result"),
                    },
                    A2AMethods.SendStreamingMessage => new StreamResponse
                    {
                        Message = CreateMessage("stream-result"),
                    },
                    A2AMethods.GetTask => CreateTask("task-1"),
                    A2AMethods.ListTasks => new ListTasksResponse { Tasks = [] },
                    A2AMethods.CancelTask => CreateTask("task-2"),
                    A2AMethods.SubscribeToTask => new StreamResponse
                    {
                        Task = CreateTask("task-3"),
                    },
                    A2AMethods.CreateTaskPushNotificationConfig =>
                        CreatePushConfig("task-4", "config-1"),
                    A2AMethods.GetTaskPushNotificationConfig =>
                        CreatePushConfig("task-4", "config-1"),
                    A2AMethods.ListTaskPushNotificationConfigs =>
                        new ListTaskPushNotificationConfigsResponse(),
                    A2AMethods.DeleteTaskPushNotificationConfig => null,
                    A2AMethods.GetExtendedAgentCard => CreateAgentCard(),
                    _ => throw new InvalidOperationException(method),
                };

                return CreateJsonRpcResponse(
                    root.GetProperty("id").GetRawText(),
                    result,
                    method is A2AMethods.SendStreamingMessage
                        or A2AMethods.SubscribeToTask);
            })));

        var sendRequest = new SendMessageRequest
        {
            Message = CreateMessage("send-request"),
        };
        await client.SendMessageAsync(sendRequest);
        await EnumerateAsync(client.SendStreamingMessageAsync(sendRequest));
        await client.GetTaskAsync(
            new GetTaskRequest { Id = "task/1", HistoryLength = 3 });
        await client.ListTasksAsync(
            new ListTasksRequest
            {
                ContextId = "context-1",
                PageSize = 10,
                PageToken = "next",
                HistoryLength = 2,
                IncludeArtifacts = true,
            });
        await client.CancelTaskAsync(
            new CancelTaskRequest
            {
                Id = "task-2",
                Metadata = new Dictionary<string, JsonElement>
                {
                    ["reason"] = JsonSerializer.SerializeToElement("user"),
                },
            });
        await EnumerateAsync(
            client.SubscribeToTaskAsync(
                new SubscribeToTaskRequest { Id = "task-3" }));
        await client.CreateTaskPushNotificationConfigAsync(
            CreatePushConfig("task-4", "config-1"));
        await client.GetTaskPushNotificationConfigAsync(
            new GetTaskPushNotificationConfigRequest
            {
                TaskId = "task-4",
                Id = "config-1",
            });
        await client.ListTaskPushNotificationConfigsAsync(
            new ListTaskPushNotificationConfigsRequest
            {
                TaskId = "task-4",
                PageSize = 5,
                PageToken = "page",
            });
        Task deleteTask = client.DeleteTaskPushNotificationConfigAsync(
            new DeleteTaskPushNotificationConfigRequest
            {
                TaskId = "task-4",
                Id = "config-1",
            });
        await deleteTask;
        await client.GetExtendedAgentCardAsync(
            new GetExtendedAgentCardRequest());

        Assert.Equal(
            [
                A2AMethods.SendMessage,
                A2AMethods.SendStreamingMessage,
                A2AMethods.GetTask,
                A2AMethods.ListTasks,
                A2AMethods.CancelTask,
                A2AMethods.SubscribeToTask,
                A2AMethods.CreateTaskPushNotificationConfig,
                A2AMethods.GetTaskPushNotificationConfig,
                A2AMethods.ListTaskPushNotificationConfigs,
                A2AMethods.DeleteTaskPushNotificationConfig,
                A2AMethods.GetExtendedAgentCard,
            ],
            requests.Select(static request => request.JsonRpcMethod));
        Assert.All(
            requests,
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("http://localhost/a2a", request.Uri);
                Assert.Equal("1.0", request.A2AVersion);
                Assert.Equal("2.0", request.JsonRpcVersion);
                Assert.True(Guid.TryParse(request.JsonRpcId, out _));
            });
        Assert.Equal(
            "send-request",
            requests[0].JsonBody
                .GetProperty("params")
                .GetProperty("message")
                .GetProperty("messageId")
                .GetString());
        Assert.Equal(
            "task/1",
            requests[2].JsonBody
                .GetProperty("params")
                .GetProperty("id")
                .GetString());
        Assert.Equal(
            "user",
            requests[4].JsonBody
                .GetProperty("params")
                .GetProperty("metadata")
                .GetProperty("reason")
                .GetString());
        Assert.Equal(
            "task-4",
            requests[6].JsonBody
                .GetProperty("params")
                .GetProperty("taskId")
                .GetString());
        Assert.Equal(
            "text/event-stream",
            requests[1].Accept);
        Assert.Equal(
            "text/event-stream",
            requests[5].Accept);
    }

    [Fact]
    public async Task HttpJsonConvenienceMethods_UseStandardOperationBindings()
    {
        var requests = new List<CapturedRequest>();
        var client = new A2AHttpJsonClient(
            new Uri("http://localhost/a2a/v1"),
            CreateStandardBindings(),
            new HttpClient(new RecordingHandler(request =>
            {
                var body = request.Content?.ReadAsStringAsync()
                    .GetAwaiter()
                    .GetResult();
                requests.Add(CapturedRequest.Create(request, body));
                var path = request.RequestUri!.AbsolutePath;
                object? result = path switch
                {
                    "/a2a/v1/message:send" => new SendMessageResponse
                    {
                        Message = CreateMessage("send-result"),
                    },
                    "/a2a/v1/message:stream" => new StreamResponse
                    {
                        Message = CreateMessage("stream-result"),
                    },
                    "/a2a/v1/tasks/task%2F1" => CreateTask("task/1"),
                    "/a2a/v1/tasks" => new ListTasksResponse { Tasks = [] },
                    "/a2a/v1/tasks/task-2:cancel" => CreateTask("task-2"),
                    "/a2a/v1/tasks/task-3:subscribe" => new StreamResponse
                    {
                        Task = CreateTask("task-3"),
                    },
                    "/a2a/v1/tasks/task-4/pushNotificationConfigs"
                        when request.Method == HttpMethod.Post =>
                            CreatePushConfig("task-4", "config-1"),
                    "/a2a/v1/tasks/task-4/pushNotificationConfigs"
                        when request.Method == HttpMethod.Get =>
                            new ListTaskPushNotificationConfigsResponse(),
                    "/a2a/v1/tasks/task-4/pushNotificationConfigs/config-1"
                        when request.Method == HttpMethod.Get =>
                            CreatePushConfig("task-4", "config-1"),
                    "/a2a/v1/tasks/task-4/pushNotificationConfigs/config-1"
                        when request.Method == HttpMethod.Delete => null,
                    "/a2a/v1/extendedAgentCard" => CreateAgentCard(),
                    _ => throw new InvalidOperationException(
                        $"{request.Method} {request.RequestUri}"),
                };

                if (request.Method == HttpMethod.Delete)
                {
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                var json = JsonSerializer.Serialize(
                    result,
                    A2AJsonUtilities.DefaultOptions);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        path.EndsWith(":stream", StringComparison.Ordinal)
                            || path.EndsWith(":subscribe", StringComparison.Ordinal)
                                ? $"data: {json}\n\n"
                                : json,
                        Encoding.UTF8,
                        path.EndsWith(":stream", StringComparison.Ordinal)
                            || path.EndsWith(":subscribe", StringComparison.Ordinal)
                                ? "text/event-stream"
                                : "application/json"),
                };
            })));

        var sendRequest = new SendMessageRequest
        {
            Message = CreateMessage("send-request"),
        };
        await client.SendMessageAsync(sendRequest);
        await EnumerateAsync(client.SendStreamingMessageAsync(sendRequest));
        await client.GetTaskAsync(
            new GetTaskRequest { Id = "task/1", HistoryLength = 3 });
        await client.ListTasksAsync(
            new ListTasksRequest
            {
                ContextId = "context 1",
                Status = TaskState.Completed,
                PageSize = 10,
                PageToken = "next/page",
                HistoryLength = 2,
                StatusTimestampAfter =
                    new DateTimeOffset(
                        2026,
                        1,
                        15,
                        12,
                        0,
                        0,
                        TimeSpan.Zero),
                IncludeArtifacts = true,
            });
        await client.CancelTaskAsync(
            new CancelTaskRequest
            {
                Id = "task-2",
                Metadata = new Dictionary<string, JsonElement>
                {
                    ["reason"] = JsonSerializer.SerializeToElement("user"),
                },
            });
        await EnumerateAsync(
            client.SubscribeToTaskAsync(
                new SubscribeToTaskRequest { Id = "task-3" }));
        await client.CreateTaskPushNotificationConfigAsync(
            CreatePushConfig("task-4", "config-1"));
        await client.GetTaskPushNotificationConfigAsync(
            new GetTaskPushNotificationConfigRequest
            {
                TaskId = "task-4",
                Id = "config-1",
            });
        await client.ListTaskPushNotificationConfigsAsync(
            new ListTaskPushNotificationConfigsRequest
            {
                TaskId = "task-4",
                PageSize = 5,
                PageToken = "page/token",
            });
        Task deleteTask = client.DeleteTaskPushNotificationConfigAsync(
            new DeleteTaskPushNotificationConfigRequest
            {
                TaskId = "task-4",
                Id = "config-1",
            });
        await deleteTask;
        await client.GetExtendedAgentCardAsync(
            new GetExtendedAgentCardRequest());

        string[] expectedRequests =
            [
                "POST http://localhost/a2a/v1/message:send",
                "POST http://localhost/a2a/v1/message:stream",
                "GET http://localhost/a2a/v1/tasks/task%2F1?historyLength=3",
                "GET http://localhost/a2a/v1/tasks?contextId=context%201&status=TASK_STATE_COMPLETED&pageSize=10&pageToken=next%2Fpage&historyLength=2&statusTimestampAfter=2026-01-15T12%3A00%3A00.0000000%2B00%3A00&includeArtifacts=true",
                "POST http://localhost/a2a/v1/tasks/task-2:cancel",
                "POST http://localhost/a2a/v1/tasks/task-3:subscribe",
                "POST http://localhost/a2a/v1/tasks/task-4/pushNotificationConfigs",
                "GET http://localhost/a2a/v1/tasks/task-4/pushNotificationConfigs/config-1",
                "GET http://localhost/a2a/v1/tasks/task-4/pushNotificationConfigs?pageSize=5&pageToken=page%2Ftoken",
                "DELETE http://localhost/a2a/v1/tasks/task-4/pushNotificationConfigs/config-1",
                "GET http://localhost/a2a/v1/extendedAgentCard",
            ];
        var actualRequests = requests.Select(
            static request => $"{request.Method} {request.Uri}").ToArray();
        Assert.True(
            expectedRequests.SequenceEqual(actualRequests),
            $"Expected:{Environment.NewLine}{string.Join(Environment.NewLine, expectedRequests)}{Environment.NewLine}Actual:{Environment.NewLine}{string.Join(Environment.NewLine, actualRequests)}");
        Assert.All(
            requests,
            request => Assert.Equal("1.0", request.A2AVersion));
        Assert.Equal(
            "application/json",
            requests[0].ContentType);
        Assert.Equal(
            "send-request",
            requests[0].JsonBody
                .GetProperty("message")
                .GetProperty("messageId")
                .GetString());
        Assert.Equal(
            "text/event-stream",
            requests[1].Accept);
        Assert.Equal(
            "user",
            requests[4].JsonBody
                .GetProperty("metadata")
                .GetProperty("reason")
                .GetString());
        Assert.Null(requests[5].Body);
        Assert.Equal(
            "text/event-stream",
            requests[5].Accept);
        Assert.Equal(
            "task-4",
            requests[6].JsonBody
                .GetProperty("taskId")
                .GetString());
        Assert.Null(requests[9].Body);
        Assert.Null(requests[10].Body);
    }

    [Fact]
    public async Task JsonRpcConvenienceMethod_RunsSemanticValidatorBeforeSending()
    {
        var requestCount = 0;
        var client = new A2AClient(
            new Uri("http://localhost/a2a"),
            CreateStandardBindings(),
            new HttpClient(new RecordingHandler(_ =>
            {
                requestCount++;
                throw new InvalidOperationException("Request should not be sent.");
            })));

        var exception = await Assert.ThrowsAsync<A2AException>(
            () => client.GetTaskAsync(
                new GetTaskRequest
                {
                    Id = "task-1",
                    HistoryLength = -1,
                }));

        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task HttpJsonStreamingConvenienceMethod_IsLazyAndValidatesBeforeSending()
    {
        var requestCount = 0;
        var client = new A2AHttpJsonClient(
            new Uri("http://localhost/a2a"),
            CreateStandardBindings(),
            new HttpClient(new RecordingHandler(_ =>
            {
                requestCount++;
                throw new InvalidOperationException("Request should not be sent.");
            })));

        var stream = client.SendStreamingMessageAsync(
            new SendMessageRequest
            {
                Message = new Message
                {
                    MessageId = "empty",
                    Role = Role.User,
                    Parts = [],
                },
            });

        Assert.Equal(0, requestCount);
        var exception = await Assert.ThrowsAsync<A2AException>(
            () => EnumerateAsync(stream));

        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public void Build_PublicBindingCannotReplaceCanonicalStandardBinding()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var catalog = operationBuilder.Build();
        var bindingBuilder = new A2AClientOperationBindingBuilder()
            .MapJsonRpc(
                standard.GetTask,
                "custom/get-task",
                GetTypeInfo<GetTaskRequest>(),
                GetTypeInfo<AgentTask>());

        var exception = Assert.Throws<InvalidOperationException>(
            () => bindingBuilder.Build(catalog));

        Assert.Contains(
            "canonical standard client binding contributor",
            exception.Message);
    }

    private static A2AClientOperationBindings CreateStandardBindings()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var catalog = operationBuilder.Build();
        return new A2AClientOperationBindingBuilder()
            .AddStandardA2AJsonRpcBindings(standard)
            .AddStandardA2AHttpBindings(standard)
            .Build(catalog);
    }

    private static Message CreateMessage(string id) =>
        new()
        {
            MessageId = id,
            Role = Role.User,
            Parts = [Part.FromText("hello")],
        };

    private static AgentTask CreateTask(string id) =>
        new()
        {
            Id = id,
            ContextId = "context",
            Status = new TaskStatus { State = TaskState.Working },
        };

    private static TaskPushNotificationConfig CreatePushConfig(
        string taskId,
        string id) =>
        new()
        {
            TaskId = taskId,
            Id = id,
            Url = "http://localhost/push",
        };

    private static AgentCard CreateAgentCard() =>
        new()
        {
            Name = "agent",
            Description = "agent",
            Version = "1.0",
        };

    private static System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>
        GetTypeInfo<T>() =>
        (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)
            A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

    private static HttpResponseMessage CreateJsonRpcResponse(
        string id,
        object? result,
        bool isSse)
    {
        var resultJson = JsonSerializer.Serialize(
            result,
            A2AJsonUtilities.DefaultOptions);
        var responseJson =
            $$"""{"jsonrpc":"2.0","id":{{id}},"result":{{resultJson}}}""";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                isSse ? $"data: {responseJson}\n\n" : responseJson,
                Encoding.UTF8,
                isSse ? "text/event-stream" : "application/json"),
        };
    }

    private static async Task EnumerateAsync<T>(
        IAsyncEnumerable<T> items,
        CancellationToken cancellationToken = default)
    {
        await foreach (var _ in items.WithCancellation(cancellationToken))
        {
        }
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class CapturedRequest
    {
        private CapturedRequest(
            HttpMethod method,
            string uri,
            string? body,
            string? contentType,
            string? accept,
            string? a2aVersion,
            JsonDocument? jsonDocument)
        {
            Method = method;
            Uri = uri;
            Body = body;
            ContentType = contentType;
            Accept = accept;
            A2AVersion = a2aVersion;
            JsonDocument = jsonDocument;
        }

        internal HttpMethod Method { get; }

        internal string Uri { get; }

        internal string? Body { get; }

        internal string? ContentType { get; }

        internal string? Accept { get; }

        internal string? A2AVersion { get; }

        internal JsonElement JsonBody => JsonDocument!.RootElement;

        internal string? JsonRpcMethod =>
            JsonDocument?.RootElement.GetProperty("method").GetString();

        internal string? JsonRpcVersion =>
            JsonDocument?.RootElement.GetProperty("jsonrpc").GetString();

        internal string? JsonRpcId =>
            JsonDocument?.RootElement.GetProperty("id").GetString();

        private JsonDocument? JsonDocument { get; }

        internal static CapturedRequest Create(
            HttpRequestMessage request,
            string? body)
        {
            var jsonDocument = string.IsNullOrEmpty(body)
                ? null
                : JsonDocument.Parse(body);
            return new CapturedRequest(
                request.Method,
                request.RequestUri!.OriginalString,
                body,
                request.Content?.Headers.ContentType?.MediaType,
                request.Headers.Accept.SingleOrDefault()?.MediaType,
                request.Headers.TryGetValues("A2A-Version", out var versions)
                    ? versions.Single()
                    : null,
                jsonDocument);
        }
    }
}

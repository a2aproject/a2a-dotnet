using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using A2A.AspNetCore;
using System.Text;
using System.Text.Json;

namespace A2A.V0_3Compat.UnitTests;

/// <summary>
/// Tests for <see cref="V03ServerProcessor.ProcessRequestAsync"/>:
/// - v0.3 method names are translated to v1.0 and responses are converted back to v0.3 wire format
/// - v1.0 method names bypass the V03 deserializer and route directly to the v1.0 processor
/// - Unsupported A2A-Version header returns -32009 (delegated to A2AJsonRpcProcessor.CheckPreflight)
/// </summary>
public class V03ServerProcessorTests
{
    // ── Version negotiation ──────────────────────────────────────────────────

    [Fact]
    public async Task ProcessRequestAsync_UnsupportedVersionHeader_Returns32009()
    {
        // Tests commit 06c0417: CheckPreflight delegation — V03ServerProcessor must return
        // -32009 for unsupported versions without touching the request body.
        var handler = CreateRequestHandler();
        var request = CreateHttpRequest(
            """{"jsonrpc":"2.0","method":"message/send","id":1,"params":{"message":{"kind":"agentMessage","messageId":"m1","role":"user","parts":[{"kind":"text","text":"hi"}]}}}""",
            version: "99.0");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        Assert.Equal(-32009, body.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task ProcessRequestAsync_V03VersionHeader_V03Body_Succeeds()
    {
        // A2A-Version: 0.3 is accepted; v0.3 body must be processed normally (no -32009).
        var handler = CreateRequestHandler();
        var request = CreateHttpRequest(
            """{"jsonrpc":"2.0","method":"message/send","id":1,"params":{"message":{"kind":"agentMessage","messageId":"m1","role":"user","parts":[{"kind":"text","text":"hi"}]}}}""",
            version: "0.3");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        Assert.False(body.RootElement.TryGetProperty("error", out _),
            "Expected no error for A2A-Version: 0.3 with v0.3 body");
    }

    [Fact]
    public async Task ProcessRequestAsync_V1VersionHeader_V1Method_Succeeds()
    {
        // A2A-Version: 1.0 is accepted; v1.0 method must be routed to the v1.0 processor.
        var handler = CreateRequestHandler();
        var request = CreateHttpRequest(
            """{"jsonrpc":"2.0","method":"SendMessage","id":1,"params":{"message":{"messageId":"m1","role":"ROLE_USER","parts":[{"text":"hi"}]}}}""",
            version: "1.0");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        Assert.False(body.RootElement.TryGetProperty("error", out _),
            "Expected no error for A2A-Version: 1.0 with v1.0 method");
    }

    // ── v0.3 method routing ──────────────────────────────────────────────────

    [Fact]
    public async Task ProcessRequestAsync_V03MessageSend_ReturnsV03WireFormat()
    {
        // v0.3 'message/send' must be translated and the response returned in v0.3 wire format:
        // - result is at the top level (not result.task)
        // - state uses kebab-case / lowercase, NOT SCREAMING_SNAKE_CASE
        var handler = CreateRequestHandler();
        var request = CreateHttpRequest(
            """{"jsonrpc":"2.0","method":"message/send","id":1,"params":{"message":{"kind":"agentMessage","messageId":"m1","role":"user","parts":[{"kind":"text","text":"Who is my manager?"}]}}}""");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        var rawText = body.RootElement.GetRawText();
        Assert.True(body.RootElement.TryGetProperty("result", out _),
            "Expected 'result' at top level for v0.3 message/send");
        Assert.False(body.RootElement.TryGetProperty("error", out _),
            "Expected no error for valid v0.3 message/send");
        Assert.DoesNotContain("TASK_STATE_", rawText);
    }

    [Fact]
    public async Task ProcessRequestAsync_V03TasksGet_NonExistentTask_ReturnsTaskNotFoundError()
    {
        var handler = CreateRequestHandler();
        var request = CreateHttpRequest(
            """{"jsonrpc":"2.0","method":"tasks/get","id":1,"params":{"id":"nonexistent-task-v03"}}""");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        Assert.True(body.RootElement.TryGetProperty("error", out var errorEl),
            "Expected error for nonexistent task");
        Assert.Equal(-32001, errorEl.GetProperty("code").GetInt32()); // TaskNotFound
    }

    [Fact]
    public async Task ProcessRequestAsync_V03TasksCancel_NonExistentTask_ReturnsTaskNotFoundError()
    {
        var handler = CreateRequestHandler();
        var request = CreateHttpRequest(
            """{"jsonrpc":"2.0","method":"tasks/cancel","id":1,"params":{"id":"nonexistent-task-v03"}}""");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        Assert.True(body.RootElement.TryGetProperty("error", out var errorEl),
            "Expected error for nonexistent task");
        Assert.Equal(-32001, errorEl.GetProperty("code").GetInt32()); // TaskNotFound
    }

    [Fact]
    public async Task ProcessRequestAsync_V03TasksResubscribe_NonExistentTask_ReturnsTaskNotFoundError()
    {
        var handler = CreateRequestHandler();
        var request = CreateHttpRequest(
            """{"jsonrpc":"2.0","method":"tasks/resubscribe","id":1,"params":{"id":"nonexistent-task-v03"}}""");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        Assert.True(body.RootElement.TryGetProperty("error", out var errorEl),
            "Expected error for nonexistent task");
        Assert.Equal(-32001, errorEl.GetProperty("code").GetInt32()); // TaskNotFound
    }

    // ── v1.0 method routing through V03ServerProcessor ──────────────────────

    [Fact]
    public async Task ProcessRequestAsync_V1MethodName_RoutesDirectlyToV1Processor()
    {
        // v1.0 clients send A2A-Version: 1.0; the processor routes to HandleV1RequestAsync.
        var handler = CreateRequestHandler();
        var request = CreateHttpRequest(
            """{"jsonrpc":"2.0","method":"SendMessage","id":1,"params":{"message":{"messageId":"m1","role":"ROLE_USER","parts":[{"text":"hi"}]}}}""",
            version: "1.0");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        var rawText = body.RootElement.GetRawText();
        Assert.False(body.RootElement.TryGetProperty("error", out _),
            "v1.0 SendMessage through V03ServerProcessor must succeed");
        Assert.Contains("TASK_STATE_", rawText);
    }

    [Fact]
    public async Task ProcessRequestAsync_V1SendMessage_ClearsTenantFieldsBeforeDispatch()
    {
        var handler = new RecordingRequestHandler();
        var request = CreateHttpRequest(
            """{"jsonrpc":"2.0","method":"SendMessage","id":1,"params":{"tenant":"top-level","message":{"messageId":"m1","role":"ROLE_USER","parts":[{"text":"hi"}]},"configuration":{"taskPushNotificationConfig":{"url":"https://push.example","tenant":"embedded"}}}}""",
            version: "1.0");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        Assert.IsType<JsonRpcResponseResult>(result);
        Assert.NotNull(handler.LastSendMessageRequest);
        Assert.Null(handler.LastSendMessageRequest.Tenant);
        Assert.Null(handler.LastSendMessageRequest.Configuration!.TaskPushNotificationConfig!.Tenant);
    }

    [Fact]
    public async Task ProcessRequestAsync_V1StreamingSend_ClearsTenantFieldsBeforeDispatch()
    {
        var handler = new RecordingRequestHandler();
        var request = CreateHttpRequest(
            """{"jsonrpc":"2.0","method":"SendStreamingMessage","id":1,"params":{"tenant":"top-level","message":{"messageId":"m1","role":"ROLE_USER","parts":[{"text":"hi"}]},"configuration":{"taskPushNotificationConfig":{"url":"https://push.example","tenant":"embedded"}}}}""",
            version: "1.0");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        Assert.IsType<JsonRpcStreamedResult>(result);
        Assert.NotNull(handler.LastStreamingSendRequest);
        Assert.Null(handler.LastStreamingSendRequest.Tenant);
        Assert.Null(handler.LastStreamingSendRequest.Configuration!.TaskPushNotificationConfig!.Tenant);
    }

    [Fact]
    public async Task ProcessRequestAsync_V1GetTaskMethod_RoutesDirectlyToV1Processor()
    {
        var handler = CreateRequestHandler();
        var request = CreateHttpRequest(
            """{"jsonrpc":"2.0","method":"GetTask","id":1,"params":{"id":"nonexistent-task"}}""",
            version: "1.0");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        // v1.0 GetTask for nonexistent task returns -32001 in v1.0 wire format
        Assert.True(body.RootElement.TryGetProperty("error", out var errorEl),
            "Expected error for nonexistent task via v1.0 GetTask");
        Assert.Equal(-32001, errorEl.GetProperty("code").GetInt32()); // TaskNotFound
    }

    // ── Malformed requests ───────────────────────────────────────────────────

    public static IEnumerable<object?[]> DuplicatePropertyRequests()
    {
        string[] requests =
        [
            """{"method":"tasks/get","method":"message/send","params":{}}""",
            """{"method":"message/send","params":{},"params":{"message":{}}}""",
            """{"id":1,"id":2,"method":"tasks/get","params":{"id":"t"}}""",
            """{"jsonrpc":"2.0","jsonrpc":"2.0","method":"tasks/get","params":{"id":"t"}}""",
            """{"method":"tasks/get","params":{"id":"one","id":"two"}}""",
            """{"method":"message/send","params":{"message":{},"message":{}}}""",
            """{"method":"message/send","params":{"message":{"taskId":"one","taskId":"two"}}}""",
            """{"method":"message/stream","params":{"message":{"taskId":"one","taskId":"two"}}}""",
            """{"method":"tasks/resubscribe","params":{"id":"one","id":"two"}}""",
            """{"method":"message/send","params":{"message":{"parts":[{"kind":"text","text":"one","text":"two"}]}}}""",
            """{"method":"message/send","params":{"metadata":{"items":[{"key":1,"key":2}]}}}""",
            """{"method":"message/send","params":{"metadata":{"key":1,"Key":2}}}""",
            """{"method":"tasks/get","Method":"message/send","params":{"id":"t"}}""",
            """{"method":"tasks/get","params":{"id":"one","Id":"two"}}""",
            """{"method":"message/send","params":{"message":{"taskId":"one","TaskId":"two"}}}""",
            """{"method":"tasks/get","params":{"id":"one","\u0069d":"two"}}"""
        ];

        foreach (var version in new string?[] { null, "0.3", "1.0" })
        {
            foreach (var json in requests)
            {
                var request = version == "1.0"
                    ? json.Replace("tasks/get", "GetTask", StringComparison.Ordinal)
                        .Replace("message/send", "SendMessage", StringComparison.Ordinal)
                        .Replace("message/stream", "SendStreamingMessage", StringComparison.Ordinal)
                        .Replace("tasks/resubscribe", "SubscribeToTask", StringComparison.Ordinal)
                    : json;
                yield return ["""{"jsonrpc":"2.0","id":"request",""" + request[1..], version];
            }
        }
    }

    [Theory]
    [MemberData(nameof(DuplicatePropertyRequests))]
    public async Task ProcessRequestAsync_DuplicateProperties_ReturnsInvalidRequestWithoutInvokingHandler(
        string json, string? version)
    {
        await using var handler = new TrackingRequestHandler();
        var result = await V03ServerProcessor.ProcessRequestAsync(
            handler, CreateHttpRequest(json, version), CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        Assert.Equal(-32600, body.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("id").ValueKind);
        Assert.False(body.RootElement.TryGetProperty("result", out _));
        Assert.False(handler.Invoked);

        var responseJson = """{"jsonrpc":"2.0","id":"first","id":"last","result":{}}""";
        if (version == "1.0")
        {
            var response = JsonSerializer.Deserialize<JsonRpcResponse>(responseJson, A2AJsonUtilities.DefaultOptions);
            Assert.Equal("last", response!.Id.AsString());
        }
        else
        {
            var response = JsonSerializer.Deserialize<A2A.V0_3.JsonRpcResponse>(
                responseJson, A2A.V0_3.A2AJsonUtilities.DefaultOptions);
            Assert.Equal("last", response!.Id.AsString());
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0.3")]
    [InlineData("1.0")]
    public async Task ProcessRequestAsync_DuplicateFollowedByMalformedJson_ReturnsParseError(string? version)
    {
        await using var handler = new TrackingRequestHandler();
        var request = CreateHttpRequest("""{"method":"tasks/get","method":"tasks/get",""", version);
        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        Assert.Equal(-32700, body.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.False(handler.Invoked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0.3")]
    [InlineData("1.0")]
    public async Task ProcessRequestAsync_ValidCasingAndRepeatedNamesInDifferentObjects_Succeeds(string? version)
    {
        var isV1 = version == "1.0";
        var method = isV1 ? "SendMessage" : "message/send";
        var role = isV1 ? "ROLE_USER" : "user";
        var json = $$$"""
            {
                "jsonrpc": "2.0", "id": "valid", "method": "{{{method}}}",
                "params": {
                    "Message": {
                        "kind": "message", "MessageId": "m", "Role": "{{{role}}}",
                        "Parts": [{"kind": "text", "text": "one"}, {"kind": "text", "text": "two"}]
                    },
                    "metadata": {"one": {"key": 1}, "two": {"key": 2}}
                }
            }
            """;
        var result = await V03ServerProcessor.ProcessRequestAsync(
            CreateRequestHandler(), CreateHttpRequest(json, version), CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        Assert.False(body.RootElement.TryGetProperty("error", out _), body.RootElement.GetRawText());
        Assert.True(body.RootElement.TryGetProperty("result", out _));
        Assert.Equal("valid", body.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task ProcessRequestAsync_InvalidJson_ReturnsParseError()
    {
        var handler = CreateRequestHandler();
        var request = CreateHttpRequest("this is not valid json");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        Assert.True(body.RootElement.TryGetProperty("error", out var errorEl));
        Assert.Equal(-32700, errorEl.GetProperty("code").GetInt32()); // ParseError
    }

    [Fact]
    public async Task ProcessRequestAsync_MissingMethodField_ReturnsInvalidRequest()
    {
        // JSON is valid but "method" field is absent → InvalidRequest (-32600), not ParseError (-32700).
        var handler = CreateRequestHandler();
        var request = CreateHttpRequest("""{"jsonrpc":"2.0","id":1,"params":{}}""");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        Assert.True(body.RootElement.TryGetProperty("error", out var errorEl));
        Assert.Equal(-32600, errorEl.GetProperty("code").GetInt32()); // InvalidRequest, not ParseError
    }

    [Fact]
    public async Task ProcessRequestAsync_V1Path_MissingMethodField_ReturnsInvalidRequest()
    {
        // v1.0 request with valid JSON but missing "method" → InvalidRequest (-32600).
        var handler = CreateRequestHandler();
        var request = CreateHttpRequest("""{"jsonrpc":"2.0","id":1,"params":{}}""", version: "1.0");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        Assert.True(body.RootElement.TryGetProperty("error", out var errorEl));
        Assert.Equal(-32600, errorEl.GetProperty("code").GetInt32()); // InvalidRequest, not ParseError
    }

    [Fact]
    public async Task ProcessRequestAsync_UnknownV03Method_ReturnsMethodNotFoundError()
    {
        var handler = CreateRequestHandler();
        var request = CreateHttpRequest(
            """{"jsonrpc":"2.0","method":"tasks/unknown","id":1,"params":{}}""");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        Assert.True(body.RootElement.TryGetProperty("error", out var errorEl));
        Assert.Equal(-32601, errorEl.GetProperty("code").GetInt32()); // MethodNotFound
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static A2AServer CreateRequestHandler()
    {
        var notifier = new ChannelEventNotifier();
        var store = new InMemoryTaskStore();
        var agentHandler = new MinimalAgentHandler();
        return new A2AServer(agentHandler, store, notifier, NullLogger<A2AServer>.Instance);
    }

    private static HttpRequest CreateHttpRequest(string json, string? version = null)
    {
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentType = "application/json";
        if (version is not null)
            context.Request.Headers["A2A-Version"] = version;
        return context.Request;
    }

    private static async Task<JsonDocument> ExecuteAndParseJson(IResult result)
    {
        var context = new DefaultHttpContext();
        var ms = new MemoryStream();
        context.Response.Body = ms;
        await result.ExecuteAsync(context);
        ms.Position = 0;
        return await JsonDocument.ParseAsync(ms);
    }

    [Fact]
    public async Task CustomHandler_RetainsItsExistingLegacyPushBehavior()
    {
        await using var handler = new CustomPushHandler();
        var request = CreateHttpRequest(
            """{"jsonrpc":"2.0","method":"tasks/pushNotificationConfig/set","id":73,"params":{"taskId":"custom","pushNotificationConfig":{"id":"cfg","url":"https://unused.example/"}}}""",
            version: "0.3");
        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);
        using var body = await ExecuteAndParseJson(result);
        Assert.Equal(73, body.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("custom", body.RootElement.GetProperty("result").GetProperty("taskId").GetString());
        Assert.False(body.RootElement.TryGetProperty("error", out _));
    }

    private sealed class TrackingRequestHandler()
        : A2AServer(new MinimalAgentHandler(), new InMemoryTaskStore(), new ChannelEventNotifier(), NullLogger<A2AServer>.Instance)
    {
        public bool Invoked { get; private set; }

        public override Task<SendMessageResponse> SendMessageAsync(
            SendMessageRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) => throw UnexpectedCall();

        public override Task<AgentTask> GetTaskAsync(
            GetTaskRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) => throw UnexpectedCall();

        public override Task<ListTasksResponse> ListTasksAsync(
            ListTasksRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) => throw UnexpectedCall();

        public override Task<AgentTask> CancelTaskAsync(
            CancelTaskRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) => throw UnexpectedCall();

        public override IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(
            SendMessageRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) => throw UnexpectedCall();

        public override IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(
            SubscribeToTaskRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) => throw UnexpectedCall();

        private InvalidOperationException UnexpectedCall()
        {
            Invoked = true;
            return new InvalidOperationException("The request handler must not be invoked.");
        }
    }

    private sealed class CustomPushHandler()
        : A2AServer(new MinimalAgentHandler(), new InMemoryTaskStore(), new ChannelEventNotifier(), NullLogger<A2AServer>.Instance)
    {
        public override Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(
            TaskPushNotificationConfig config, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) => Task.FromResult(config);
    }

    private sealed class MinimalAgentHandler : IAgentHandler
    {
        public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            var task = new AgentTask
            {
                Id = context.TaskId,
                ContextId = context.ContextId,
                Status = new TaskStatus { State = TaskState.Completed },
                History = [context.Message],
            };
            await eventQueue.EnqueueTaskAsync(task, cancellationToken);
            eventQueue.Complete();
        }

        public async Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
            await updater.CancelAsync(cancellationToken: cancellationToken);
        }
    }

    private sealed class RecordingRequestHandler : IA2ARequestHandler
    {
        internal SendMessageRequest? LastSendMessageRequest { get; private set; }
        internal SendMessageRequest? LastStreamingSendRequest { get; private set; }

        public Task<SendMessageResponse> SendMessageAsync(
            SendMessageRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default)
        {
            LastSendMessageRequest = request;
            return Task.FromResult(new SendMessageResponse
            {
                Message = new Message { MessageId = "response", Role = Role.Agent, Parts = [] },
            });
        }

        public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(
            SendMessageRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default)
        {
            LastStreamingSendRequest = request;
            return SingleStreamResponse();
        }

        public Task<AgentTask> GetTaskAsync(GetTaskRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(
            SubscribeToTaskRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(
            TaskPushNotificationConfig config, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(
            GetTaskPushNotificationConfigRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(
            ListTaskPushNotificationConfigsRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteTaskPushNotificationConfigAsync(
            DeleteTaskPushNotificationConfigRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentCard> GetExtendedAgentCardAsync(
            GetExtendedAgentCardRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static async IAsyncEnumerable<StreamResponse> SingleStreamResponse()
        {
            await Task.Yield();
            yield return new StreamResponse
            {
                Message = new Message { MessageId = "response", Role = Role.Agent, Parts = [] },
            };
        }
    }
}

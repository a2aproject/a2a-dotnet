using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task ProcessRequestAsync_MissingMethodField_ReturnsParseError()
    {
        var handler = CreateRequestHandler();
        var request = CreateHttpRequest("""{"jsonrpc":"2.0","id":1,"params":{}}""");

        var result = await V03ServerProcessor.ProcessRequestAsync(handler, request, CancellationToken.None);

        using var body = await ExecuteAndParseJson(result);
        Assert.True(body.RootElement.TryGetProperty("error", out _),
            "Request missing 'method' field must return an error");
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

    private sealed class MinimalAgentHandler : IAgentHandler
    {
        public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            var task = new AgentTask
            {
                Id = context.TaskId,
                ContextId = context.ContextId,
                Status = new TaskStatus { State = TaskState.Submitted },
                History = [context.Message],
            };
            await eventQueue.EnqueueTaskAsync(task, cancellationToken);
            eventQueue.Complete();
        }

        public async Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
            await updater.CancelAsync(cancellationToken);
        }
    }
}

using A2A.AspNetCore;
using Microsoft.AspNetCore.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace A2A.UnitTests.AspNetCore;

public class A2AJsonRpcStandardOperationTests
{
    public static TheoryData<string, A2AOperationKind> StandardMethods =>
        new()
        {
            { A2AMethods.SendMessage, A2AOperationKind.Unary },
            { A2AMethods.SendStreamingMessage, A2AOperationKind.Streaming },
            { A2AMethods.GetTask, A2AOperationKind.Unary },
            { A2AMethods.ListTasks, A2AOperationKind.Unary },
            { A2AMethods.CancelTask, A2AOperationKind.Unary },
            { A2AMethods.SubscribeToTask, A2AOperationKind.Streaming },
            { A2AMethods.CreateTaskPushNotificationConfig, A2AOperationKind.Unary },
            { A2AMethods.GetTaskPushNotificationConfig, A2AOperationKind.Unary },
            { A2AMethods.ListTaskPushNotificationConfigs, A2AOperationKind.Unary },
            { A2AMethods.DeleteTaskPushNotificationConfig, A2AOperationKind.Unary },
            { A2AMethods.GetExtendedAgentCard, A2AOperationKind.Unary },
        };

    [Theory]
    [MemberData(nameof(StandardMethods))]
    public async Task AddStandardA2AJsonRpcBindings_MapsMethodToExpectedOperationAndKind(
        string method,
        A2AOperationKind expectedKind)
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var invoked = false;
        var handlers = CreateExpectedHandlerCatalog(
            method,
            standard,
            operationCatalog,
            () => invoked = true);
        var bindings = new A2AJsonRpcOperationBindingBuilder()
            .AddStandardA2AJsonRpcBindings(standard)
            .Build(operationCatalog);
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(new ThrowingRequestHandler()),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var httpContext = CreateHttpContext(method, CreateParameters(method), 91);

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            scopeFactory,
            handlers,
            bindings,
            httpContext.Request,
            CancellationToken.None);

        Assert.Equal(0, disposeCount);

        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;
        await result.ExecuteAsync(httpContext);

        Assert.True(invoked);
        Assert.Equal(1, disposeCount);
        Assert.Equal(
            expectedKind == A2AOperationKind.Streaming
                ? "text/event-stream"
                : "application/json",
            httpContext.Response.ContentType);
        var responseText = Encoding.UTF8.GetString(responseBody.ToArray());
        Assert.Contains("\"id\":91", responseText, StringComparison.Ordinal);
        if (method == A2AMethods.DeleteTaskPushNotificationConfig)
        {
            Assert.DoesNotContain("\"result\"", responseText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ProcessRequestAsync_StandardUnaryMethodUsesHandlerCatalog()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                standard.GetTask,
                static (_, request, _) => ValueTask.FromResult(
                    new AgentTask
                    {
                        Id = request.Id,
                        ContextId = "handler-catalog",
                        Status = new TaskStatus { State = TaskState.Working },
                    }))
            .Build(operationCatalog);
        var bindings = new A2AJsonRpcOperationBindingBuilder()
            .AddStandardA2AJsonRpcBindings(standard)
            .Build(operationCatalog);
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(new ThrowingRequestHandler())));
        var httpContext = CreateHttpContext(
            A2AMethods.GetTask,
            JsonSerializer.SerializeToElement(
                new GetTaskRequest { Id = "task-1" },
                A2AJsonUtilities.DefaultOptions),
            "standard-unary");

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            scopeFactory,
            handlers,
            bindings,
            httpContext.Request,
            CancellationToken.None);
        var response = await ExecuteJsonResponseAsync(httpContext, result);
        var task = response.Result?.Deserialize<AgentTask>(
            A2AJsonUtilities.DefaultOptions);

        Assert.Equal("standard-unary", response.Id.AsString());
        Assert.Equal("handler-catalog", task?.ContextId);
    }

    [Fact]
    public async Task ProcessRequestAsync_StandardStreamingMethodUsesHandlerCatalog()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .MapStreaming(
                standard.SendStreamingMessage,
                static (_, _, cancellationToken) =>
                    YieldAsync(
                        new StreamResponse
                        {
                            Message = new Message
                            {
                                MessageId = "handler-catalog-event",
                                Role = Role.Agent,
                                Parts = [Part.FromText("streamed")],
                            },
                        },
                        cancellationToken))
            .Build(operationCatalog);
        var bindings = new A2AJsonRpcOperationBindingBuilder()
            .AddStandardA2AJsonRpcBindings(standard)
            .Build(operationCatalog);
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(new ThrowingRequestHandler())));
        var httpContext = CreateHttpContext(
            A2AMethods.SendStreamingMessage,
            CreateParameters(A2AMethods.SendStreamingMessage),
            "standard-stream");

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            scopeFactory,
            handlers,
            bindings,
            httpContext.Request,
            CancellationToken.None);
        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;
        await result.ExecuteAsync(httpContext);

        var responseText = Encoding.UTF8.GetString(responseBody.ToArray());
        Assert.Equal("text/event-stream", httpContext.Response.ContentType);
        Assert.Contains("\"id\":\"standard-stream\"", responseText, StringComparison.Ordinal);
        Assert.Contains("handler-catalog-event", responseText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessRequestAsync_StandardSemanticValidationRunsBeforeScopeCreation()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                standard.ListTasks,
                static (_, _, _) =>
                    ValueTask.FromResult(new ListTasksResponse()))
            .Build(operationCatalog);
        var bindings = new A2AJsonRpcOperationBindingBuilder()
            .AddStandardA2AJsonRpcBindings(standard)
            .Build(operationCatalog);
        var scopeCreateCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(new ThrowingRequestHandler())));
        };
        var httpContext = CreateHttpContext(
            A2AMethods.ListTasks,
            JsonSerializer.SerializeToElement(
                new ListTasksRequest { PageSize = 0 },
                A2AJsonUtilities.DefaultOptions),
            "semantic-validation");

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            scopeFactory,
            handlers,
            bindings,
            httpContext.Request,
            CancellationToken.None);
        var response = await ExecuteJsonResponseAsync(httpContext, result);

        Assert.Equal(0, scopeCreateCount);
        Assert.Equal((int)A2AErrorCode.InvalidParams, response.Error?.Code);
    }

    [Fact]
    public async Task ProcessRequestAsync_EmptyMessagePartsReturnsInvalidParamsBeforeScopeCreation()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                standard.SendMessage,
                static (_, _, _) =>
                    ValueTask.FromResult(new SendMessageResponse()))
            .Build(operationCatalog);
        var bindings = new A2AJsonRpcOperationBindingBuilder()
            .AddStandardA2AJsonRpcBindings(standard)
            .Build(operationCatalog);
        var scopeCreateCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(new ThrowingRequestHandler())));
        };
        var httpContext = CreateHttpContext(
            A2AMethods.SendMessage,
            JsonSerializer.SerializeToElement(
                new SendMessageRequest
                {
                    Message = new Message
                    {
                        MessageId = "empty-parts",
                        Role = Role.User,
                        Parts = [],
                    },
                },
                A2AJsonUtilities.DefaultOptions),
            "empty-parts");

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            scopeFactory,
            handlers,
            bindings,
            httpContext.Request,
            CancellationToken.None);
        var response = await ExecuteJsonResponseAsync(httpContext, result);

        Assert.Equal(0, scopeCreateCount);
        Assert.Equal((int)A2AErrorCode.InvalidParams, response.Error?.Code);
    }

    [Fact]
    public async Task ProcessRequestAsync_PushNotificationSupportProbePreservesNotSupportedError()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .AddStandardA2AHandlers(standard)
            .Build(operationCatalog);
        var bindings = new A2AJsonRpcOperationBindingBuilder()
            .AddStandardA2AJsonRpcBindings(standard)
            .Build(operationCatalog);
        var requestHandler = new PushNotificationNotSupportedRequestHandler();
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(new A2AOperationContext(requestHandler)));
        var httpContext = CreateHttpContext(
            A2AMethods.CreateTaskPushNotificationConfig,
            CreateParameters(A2AMethods.CreateTaskPushNotificationConfig),
            "push-not-supported");

        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(
            scopeFactory,
            handlers,
            bindings,
            httpContext.Request,
            CancellationToken.None);
        var response = await ExecuteJsonResponseAsync(httpContext, result);

        Assert.Equal(1, requestHandler.ProbeCallCount);
        Assert.Equal(0, requestHandler.CreateCallCount);
        Assert.Equal(
            (int)A2AErrorCode.PushNotificationNotSupported,
            response.Error?.Code);
    }

    private static A2AOperationHandlerCatalog CreateExpectedHandlerCatalog(
        string method,
        A2AStandardOperations standard,
        A2AOperationCatalog operationCatalog,
        Action markInvoked)
    {
        var builder = new A2AOperationHandlerCatalogBuilder();
        switch (method)
        {
            case A2AMethods.SendMessage:
                builder.Map(
                    standard.SendMessage,
                    (_, _, _) =>
                    {
                        markInvoked();
                        return ValueTask.FromResult(new SendMessageResponse());
                    });
                break;
            case A2AMethods.SendStreamingMessage:
                builder.MapStreaming(
                    standard.SendStreamingMessage,
                    (_, _, cancellationToken) =>
                    {
                        markInvoked();
                        return YieldAsync(CreateStreamResponse(), cancellationToken);
                    });
                break;
            case A2AMethods.GetTask:
                builder.Map(
                    standard.GetTask,
                    (_, request, _) =>
                    {
                        markInvoked();
                        return ValueTask.FromResult(CreateTask(request.Id));
                    });
                break;
            case A2AMethods.ListTasks:
                builder.Map(
                    standard.ListTasks,
                    (_, _, _) =>
                    {
                        markInvoked();
                        return ValueTask.FromResult(new ListTasksResponse());
                    });
                break;
            case A2AMethods.CancelTask:
                builder.Map(
                    standard.CancelTask,
                    (_, request, _) =>
                    {
                        markInvoked();
                        return ValueTask.FromResult(CreateTask(request.Id));
                    });
                break;
            case A2AMethods.SubscribeToTask:
                builder.MapStreaming(
                    standard.SubscribeToTask,
                    (_, _, cancellationToken) =>
                    {
                        markInvoked();
                        return YieldAsync(CreateStreamResponse(), cancellationToken);
                    });
                break;
            case A2AMethods.CreateTaskPushNotificationConfig:
                builder.Map(
                    standard.CreateTaskPushNotificationConfig,
                    (_, request, _) =>
                    {
                        markInvoked();
                        return ValueTask.FromResult(request);
                    });
                break;
            case A2AMethods.GetTaskPushNotificationConfig:
                builder.Map(
                    standard.GetTaskPushNotificationConfig,
                    (_, request, _) =>
                    {
                        markInvoked();
                        return ValueTask.FromResult(
                            CreatePushNotificationConfig(request.TaskId));
                    });
                break;
            case A2AMethods.ListTaskPushNotificationConfigs:
                builder.Map(
                    standard.ListTaskPushNotificationConfigs,
                    (_, _, _) =>
                    {
                        markInvoked();
                        return ValueTask.FromResult(
                            new ListTaskPushNotificationConfigsResponse());
                    });
                break;
            case A2AMethods.DeleteTaskPushNotificationConfig:
                builder.Map(
                    standard.DeleteTaskPushNotificationConfig,
                    (_, _, _) =>
                    {
                        markInvoked();
                        return ValueTask.FromResult(A2AEmptyResult.Instance);
                    });
                break;
            case A2AMethods.GetExtendedAgentCard:
                builder.Map(
                    standard.GetExtendedAgentCard,
                    (_, _, _) =>
                    {
                        markInvoked();
                        return ValueTask.FromResult(new AgentCard());
                    });
                break;
            default:
                throw new InvalidOperationException($"Unknown standard method '{method}'.");
        }

        return builder.Build(operationCatalog);
    }

    private static JsonElement CreateParameters(string method) =>
        method switch
        {
            A2AMethods.SendMessage or A2AMethods.SendStreamingMessage =>
                JsonSerializer.SerializeToElement(
                    new SendMessageRequest
                    {
                        Message = new Message
                        {
                            MessageId = "message-1",
                            Role = Role.User,
                            Parts = [Part.FromText("hello")],
                        },
                    },
                    A2AJsonUtilities.DefaultOptions),
            A2AMethods.GetTask => JsonSerializer.SerializeToElement(
                new GetTaskRequest { Id = "task-1" },
                A2AJsonUtilities.DefaultOptions),
            A2AMethods.ListTasks => JsonSerializer.SerializeToElement(
                new ListTasksRequest(),
                A2AJsonUtilities.DefaultOptions),
            A2AMethods.CancelTask => JsonSerializer.SerializeToElement(
                new CancelTaskRequest { Id = "task-1" },
                A2AJsonUtilities.DefaultOptions),
            A2AMethods.SubscribeToTask => JsonSerializer.SerializeToElement(
                new SubscribeToTaskRequest { Id = "task-1" },
                A2AJsonUtilities.DefaultOptions),
            A2AMethods.CreateTaskPushNotificationConfig =>
                JsonSerializer.SerializeToElement(
                    CreatePushNotificationConfig("task-1"),
                    A2AJsonUtilities.DefaultOptions),
            A2AMethods.GetTaskPushNotificationConfig =>
                JsonSerializer.SerializeToElement(
                    new GetTaskPushNotificationConfigRequest
                    {
                        TaskId = "task-1",
                        Id = "config-1",
                    },
                    A2AJsonUtilities.DefaultOptions),
            A2AMethods.ListTaskPushNotificationConfigs =>
                JsonSerializer.SerializeToElement(
                    new ListTaskPushNotificationConfigsRequest
                    {
                        TaskId = "task-1",
                    },
                    A2AJsonUtilities.DefaultOptions),
            A2AMethods.DeleteTaskPushNotificationConfig =>
                JsonSerializer.SerializeToElement(
                    new DeleteTaskPushNotificationConfigRequest
                    {
                        TaskId = "task-1",
                        Id = "config-1",
                    },
                    A2AJsonUtilities.DefaultOptions),
            A2AMethods.GetExtendedAgentCard => JsonSerializer.SerializeToElement(
                new GetExtendedAgentCardRequest(),
                A2AJsonUtilities.DefaultOptions),
            _ => throw new InvalidOperationException(
                $"Unknown standard method '{method}'."),
        };

    private static DefaultHttpContext CreateHttpContext(
        string method,
        JsonElement parameters,
        JsonRpcId requestId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.ContentType = "application/json";
        var request = new JsonRpcRequest
        {
            Id = requestId,
            Method = method,
            Params = parameters,
        };
        httpContext.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(
                    request,
                    A2AJsonUtilities.DefaultOptions)));
        return httpContext;
    }

    private static async Task<JsonRpcResponse> ExecuteJsonResponseAsync(
        DefaultHttpContext httpContext,
        IResult result)
    {
        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;
        await result.ExecuteAsync(httpContext);
        responseBody.Position = 0;
        return (await JsonSerializer.DeserializeAsync<JsonRpcResponse>(
            responseBody,
            A2AJsonUtilities.DefaultOptions))!;
    }

    private static AgentTask CreateTask(string id) =>
        new()
        {
            Id = id,
            ContextId = "context-1",
            Status = new TaskStatus { State = TaskState.Working },
        };

    private static StreamResponse CreateStreamResponse() =>
        new()
        {
            Message = new Message
            {
                MessageId = "event-1",
                Role = Role.Agent,
                Parts = [Part.FromText("event")],
            },
        };

    private static TaskPushNotificationConfig CreatePushNotificationConfig(
        string taskId) =>
        new()
        {
            TaskId = taskId,
            Id = "config-1",
            Url = "https://example.com/callback",
        };

    private static async IAsyncEnumerable<StreamResponse> YieldAsync(
        StreamResponse response,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return response;
        await Task.Yield();
    }

    private class ThrowingRequestHandler : IA2ARequestHandler
    {
        public virtual Task<SendMessageResponse> SendMessageAsync(
            SendMessageRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The fixed request handler was invoked.");

        public virtual IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(
            SendMessageRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The fixed request handler was invoked.");

        public virtual Task<AgentTask> GetTaskAsync(
            GetTaskRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The fixed request handler was invoked.");

        public virtual Task<ListTasksResponse> ListTasksAsync(
            ListTasksRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The fixed request handler was invoked.");

        public virtual Task<AgentTask> CancelTaskAsync(
            CancelTaskRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The fixed request handler was invoked.");

        public virtual IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(
            SubscribeToTaskRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The fixed request handler was invoked.");

        public virtual Task<TaskPushNotificationConfig>
            CreateTaskPushNotificationConfigAsync(
                TaskPushNotificationConfig config,
                CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The fixed request handler was invoked.");

        public virtual Task<TaskPushNotificationConfig>
            GetTaskPushNotificationConfigAsync(
                GetTaskPushNotificationConfigRequest request,
                CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The fixed request handler was invoked.");

        public virtual Task<ListTaskPushNotificationConfigsResponse>
            ListTaskPushNotificationConfigsAsync(
                ListTaskPushNotificationConfigsRequest request,
                CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The fixed request handler was invoked.");

        public virtual Task DeleteTaskPushNotificationConfigAsync(
            DeleteTaskPushNotificationConfigRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The fixed request handler was invoked.");

        public virtual Task<AgentCard> GetExtendedAgentCardAsync(
            GetExtendedAgentCardRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The fixed request handler was invoked.");
    }

    private sealed class PushNotificationNotSupportedRequestHandler
        : ThrowingRequestHandler
    {
        public int ProbeCallCount { get; private set; }

        public int CreateCallCount { get; private set; }

        public override Task<TaskPushNotificationConfig>
            GetTaskPushNotificationConfigAsync(
                GetTaskPushNotificationConfigRequest request,
                CancellationToken cancellationToken = default)
        {
            Assert.Null(request);
            ProbeCallCount++;
            throw new A2AException(
                "Push notifications are not supported.",
                A2AErrorCode.PushNotificationNotSupported);
        }

        public override Task<TaskPushNotificationConfig>
            CreateTaskPushNotificationConfigAsync(
                TaskPushNotificationConfig config,
                CancellationToken cancellationToken = default)
        {
            CreateCallCount++;
            return Task.FromResult(config);
        }
    }
}

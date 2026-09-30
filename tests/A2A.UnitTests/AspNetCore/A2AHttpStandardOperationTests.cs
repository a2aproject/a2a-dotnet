using A2A.AspNetCore;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace A2A.UnitTests.AspNetCore;

public class A2AHttpStandardOperationTests
{
    public static TheoryData<string, string, A2AOperationKind> StandardRoutes =>
        new()
        {
            { HttpMethods.Get, "/tasks/{id}", A2AOperationKind.Unary },
            { HttpMethods.Get, "/tasks", A2AOperationKind.Unary },
            { HttpMethods.Post, "/tasks/{id}:cancel", A2AOperationKind.Unary },
            { HttpMethods.Post, "/tasks/{id}:subscribe", A2AOperationKind.Streaming },
            { HttpMethods.Post, "/message:send", A2AOperationKind.Unary },
            { HttpMethods.Post, "/message:stream", A2AOperationKind.Streaming },
            {
                HttpMethods.Post,
                "/tasks/{id}/pushNotificationConfigs",
                A2AOperationKind.Unary
            },
            {
                HttpMethods.Get,
                "/tasks/{id}/pushNotificationConfigs",
                A2AOperationKind.Unary
            },
            {
                HttpMethods.Get,
                "/tasks/{id}/pushNotificationConfigs/{configId}",
                A2AOperationKind.Unary
            },
            {
                HttpMethods.Delete,
                "/tasks/{id}/pushNotificationConfigs/{configId}",
                A2AOperationKind.Unary
            },
            { HttpMethods.Get, "/extendedAgentCard", A2AOperationKind.Unary },
        };

    public static TheoryData<string, string?> UnsupportedJsonBodyMediaTypes =>
        new()
        {
            { "/message:send", null },
            { "/message:send", "text/plain" },
            { "/message:stream", null },
            { "/message:stream", "text/plain" },
            { "/tasks/{id}/pushNotificationConfigs", null },
            { "/tasks/{id}/pushNotificationConfigs", "text/plain" },
        };

    public static TheoryData<string, string> SupportedJsonBodyMediaTypes =>
        new()
        {
            { "/message:send", "application/json" },
            { "/message:send", "application/json; charset=utf-8" },
            { "/message:send", "application/vnd.a2a+json" },
            { "/message:stream", "application/json" },
            { "/message:stream", "application/json; charset=utf-8" },
            { "/message:stream", "application/vnd.a2a+json" },
            {
                "/tasks/{id}/pushNotificationConfigs",
                "application/json"
            },
            {
                "/tasks/{id}/pushNotificationConfigs",
                "application/json; charset=utf-8"
            },
            {
                "/tasks/{id}/pushNotificationConfigs",
                "application/vnd.a2a+json"
            },
        };

    [Theory]
    [MemberData(nameof(StandardRoutes))]
    public async Task AddStandardA2AHttpBindings_MapsRouteToExpectedTypedOperation(
        string httpMethod,
        string route,
        A2AOperationKind expectedKind)
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        object? capturedRequest = null;
        var handlers = CreateExpectedHandlerCatalog(
            httpMethod,
            route,
            standard,
            operationCatalog,
            request => capturedRequest = request);
        var bindings = new A2AHttpOperationBindingBuilder()
            .AddStandardA2AHttpBindings(standard)
            .Build();
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(new ThrowingRequestHandler()),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, httpMethod, route);
        var endpoint = GetEndpoint(app, httpMethod, route);

        await endpoint.RequestDelegate!(httpContext);

        AssertExpectedRequest(httpMethod, route, capturedRequest);
        Assert.Equal(1, disposeCount);
        Assert.Equal(
            expectedKind == A2AOperationKind.Streaming
                ? "text/event-stream"
                : route.StartsWith(
                    "/tasks/{id}/pushNotificationConfigs/{configId}",
                    StringComparison.Ordinal)
                    && httpMethod == HttpMethods.Delete
                    ? null
                    : "application/json",
            httpContext.Response.ContentType);
        Assert.Equal(
            httpMethod == HttpMethods.Delete
                ? StatusCodes.Status204NoContent
                : StatusCodes.Status200OK,
            httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task ListTasks_InvalidQueryValueReturnsInvalidParamsBeforeScopeCreation()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                standard.ListTasks,
                static (_, _, _) => ValueTask.FromResult(new ListTasksResponse()))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .AddStandardA2AHttpBindings(standard)
            .Build();
        var scopeCreateCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(new ThrowingRequestHandler())));
        };
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, HttpMethods.Get, "/tasks");
        httpContext.Request.QueryString = new QueryString("?pageSize=not-an-integer");

        await GetEndpoint(app, HttpMethods.Get, "/tasks").RequestDelegate!(httpContext);

        Assert.Equal(0, scopeCreateCount);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Contains(
            "pageSize",
            GetResponseBody(httpContext),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetTask_MissingRouteValueReturnsInvalidParamsBeforeScopeCreation()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                standard.GetTask,
                static (_, request, _) => ValueTask.FromResult(
                    CreateTask(request.Id)))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .AddStandardA2AHttpBindings(standard)
            .Build();
        var scopeCreateCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(new ThrowingRequestHandler())));
        };
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(
            app,
            HttpMethods.Get,
            "/extendedAgentCard");

        await GetEndpoint(
            app,
            HttpMethods.Get,
            "/tasks/{id}").RequestDelegate!(httpContext);

        Assert.Equal(0, scopeCreateCount);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        using var response = JsonDocument.Parse(GetResponseBody(httpContext));
        Assert.Contains(
            "route value 'id'",
            response.RootElement.GetProperty("error")
                .GetProperty("message").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListTasks_InvalidStatusPreservesProblemDetailsResponse()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                standard.ListTasks,
                static (_, _, _) => ValueTask.FromResult(new ListTasksResponse()))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .AddStandardA2AHttpBindings(standard)
            .Build();
        var scopeCreateCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(new ThrowingRequestHandler())));
        };
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, HttpMethods.Get, "/tasks");
        httpContext.Request.QueryString = new QueryString("?status=not-a-status");

        await GetEndpoint(app, HttpMethods.Get, "/tasks").RequestDelegate!(httpContext);

        Assert.Equal(0, scopeCreateCount);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Equal("application/problem+json", httpContext.Response.ContentType);
        Assert.Contains(
            "Invalid status filter",
            GetResponseBody(httpContext),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendMessage_InvalidBodyReturnsInvalidParamsBeforeScopeCreation()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                standard.SendMessage,
                static (_, _, _) => ValueTask.FromResult(new SendMessageResponse()))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .AddStandardA2AHttpBindings(standard)
            .Build();
        var scopeCreateCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(new ThrowingRequestHandler())));
        };
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(
            app,
            HttpMethods.Post,
            "/message:send");
        SetBody(httpContext, """{"message":""");

        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/message:send").RequestDelegate!(httpContext);

        Assert.Equal(0, scopeCreateCount);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.DoesNotContain(
            "Json",
            GetResponseBody(httpContext),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(UnsupportedJsonBodyMediaTypes))]
    public async Task JsonBodyRoute_UnsupportedOrMissingContentTypeReturns415BeforeScopeCreation(
        string route,
        string? contentType)
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var handlers = CreateExpectedHandlerCatalog(
            HttpMethods.Post,
            route,
            standard,
            operationCatalog,
            static _ => { });
        var bindings = new A2AHttpOperationBindingBuilder()
            .AddStandardA2AHttpBindings(standard)
            .Build();
        var scopeCreateCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(new ThrowingRequestHandler())));
        };
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, HttpMethods.Post, route);
        httpContext.Request.ContentType = contentType;

        await GetEndpoint(
            app,
            HttpMethods.Post,
            route).RequestDelegate!(httpContext);

        Assert.Equal(0, scopeCreateCount);
        Assert.Equal(
            StatusCodes.Status415UnsupportedMediaType,
            httpContext.Response.StatusCode);
        Assert.Null(httpContext.Response.ContentType);
        Assert.Empty(GetResponseBody(httpContext));
    }

    [Theory]
    [MemberData(nameof(SupportedJsonBodyMediaTypes))]
    public async Task JsonBodyRoute_SupportedContentTypeDispatches(
        string route,
        string contentType)
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        object? capturedRequest = null;
        var handlers = CreateExpectedHandlerCatalog(
            HttpMethods.Post,
            route,
            standard,
            operationCatalog,
            request => capturedRequest = request);
        var bindings = new A2AHttpOperationBindingBuilder()
            .AddStandardA2AHttpBindings(standard)
            .Build();
        var scopeCreateCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(new ThrowingRequestHandler())));
        };
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, HttpMethods.Post, route);
        httpContext.Request.ContentType = contentType;

        await GetEndpoint(
            app,
            HttpMethods.Post,
            route).RequestDelegate!(httpContext);

        Assert.NotNull(capturedRequest);
        Assert.Equal(1, scopeCreateCount);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task ListTasks_SemanticValidationRunsBeforeScopeCreation()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                standard.ListTasks,
                static (_, _, _) => ValueTask.FromResult(new ListTasksResponse()))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .AddStandardA2AHttpBindings(standard)
            .Build();
        var scopeCreateCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(new ThrowingRequestHandler())));
        };
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, HttpMethods.Get, "/tasks");
        httpContext.Request.QueryString = new QueryString("?pageSize=0");

        await GetEndpoint(app, HttpMethods.Get, "/tasks").RequestDelegate!(httpContext);

        Assert.Equal(0, scopeCreateCount);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Contains(
            "pageSize",
            GetResponseBody(httpContext),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/TASKS/{taskId}")]
    [InlineData("TASKS/{taskId}")]
    public void MapHttpA2A_WhenReservedRouteMapsToExtensionOperation_Throws(
        string route)
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var extensionOperation = operationBuilder.DefineUnary<GetTaskRequest, AgentTask>(
            new A2AOperationId("https://a2a-protocol.org/operations/get-task"));
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                extensionOperation,
                static (_, request, _) => ValueTask.FromResult(
                    CreateTask(request.Id)))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Get,
                route,
                extensionOperation,
                static (context, _) => ValueTask.FromResult(
                    new GetTaskRequest
                    {
                        Id = (string)context.Request.RouteValues["taskId"]!,
                    }),
                GetTypeInfo<AgentTask>())
            .Build();
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(new ThrowingRequestHandler())));
        var app = WebApplication.CreateBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => app.MapHttpA2A(scopeFactory, handlers, bindings));

        Assert.Contains(route, exception.Message, StringComparison.Ordinal);
        Assert.Contains("standard", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapHttpA2A_WhenReservedRouteMapsToWrongStandardOperation_Throws()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                standard.ListTasks,
                static (_, _, _) => ValueTask.FromResult(
                    new ListTasksResponse()))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Get,
                "tasks/{taskId}",
                standard.ListTasks,
                static (_, _) => ValueTask.FromResult(
                    new ListTasksRequest()),
                GetTypeInfo<ListTasksResponse>())
            .Build();
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(new ThrowingRequestHandler())));
        var app = WebApplication.CreateBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => app.MapHttpA2A(scopeFactory, handlers, bindings));

        Assert.Contains(
            "tasks/{taskId}",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            standard.GetTask.Id.Value,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Map_WhenExtensionRoutesDifferOnlyByLeadingSlash_Throws()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var firstOperation = operationBuilder.DefineUnary<
            GetTaskRequest,
            AgentTask>(new A2AOperationId("test.first-extension"));
        var secondOperation = operationBuilder.DefineUnary<
            GetTaskRequest,
            AgentTask>(new A2AOperationId("test.second-extension"));
        var bindingBuilder = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Get,
                "/extensions/{id}",
                firstOperation,
                static (_, _) => ValueTask.FromResult(
                    new GetTaskRequest { Id = "task-1" }),
                GetTypeInfo<AgentTask>());

        var exception = Assert.Throws<InvalidOperationException>(
            () => bindingBuilder.Map(
                HttpMethods.Get,
                "extensions/{taskId}",
                secondOperation,
                static (_, _) => ValueTask.FromResult(
                    new GetTaskRequest { Id = "task-1" }),
                GetTypeInfo<AgentTask>()));

        Assert.Contains(
            "extensions/{taskId}",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapHttpA2A_PushNotificationRouteDoesNotUseJsonRpcCapabilityProbe()
    {
        var requestHandler = new PushNotificationRequestHandler();
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(requestHandler);
        var httpContext = CreateHttpContext(
            app,
            HttpMethods.Post,
            "/tasks/{id}/pushNotificationConfigs");

        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/tasks/{id}/pushNotificationConfigs").RequestDelegate!(httpContext);

        Assert.Equal(0, requestHandler.ProbeCallCount);
        Assert.Equal(1, requestHandler.CreateCallCount);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
    }

    private static A2AOperationHandlerCatalog CreateExpectedHandlerCatalog(
        string httpMethod,
        string route,
        A2AStandardOperations standard,
        A2AOperationCatalog operationCatalog,
        Action<object> captureRequest)
    {
        var builder = new A2AOperationHandlerCatalogBuilder();
        switch ((httpMethod, route))
        {
            case ("GET", "/tasks/{id}"):
                builder.Map(
                    standard.GetTask,
                    (_, request, _) =>
                    {
                        captureRequest(request);
                        return ValueTask.FromResult(CreateTask(request.Id));
                    });
                break;
            case ("GET", "/tasks"):
                builder.Map(
                    standard.ListTasks,
                    (_, request, _) =>
                    {
                        captureRequest(request);
                        return ValueTask.FromResult(new ListTasksResponse());
                    });
                break;
            case ("POST", "/tasks/{id}:cancel"):
                builder.Map(
                    standard.CancelTask,
                    (_, request, _) =>
                    {
                        captureRequest(request);
                        return ValueTask.FromResult(CreateTask(request.Id));
                    });
                break;
            case ("POST", "/tasks/{id}:subscribe"):
                builder.MapStreaming(
                    standard.SubscribeToTask,
                    (_, request, cancellationToken) =>
                    {
                        captureRequest(request);
                        return YieldAsync(CreateStreamResponse(), cancellationToken);
                    });
                break;
            case ("POST", "/message:send"):
                builder.Map(
                    standard.SendMessage,
                    (_, request, _) =>
                    {
                        captureRequest(request);
                        return ValueTask.FromResult(new SendMessageResponse());
                    });
                break;
            case ("POST", "/message:stream"):
                builder.MapStreaming(
                    standard.SendStreamingMessage,
                    (_, request, cancellationToken) =>
                    {
                        captureRequest(request);
                        return YieldAsync(CreateStreamResponse(), cancellationToken);
                    });
                break;
            case ("POST", "/tasks/{id}/pushNotificationConfigs"):
                builder.Map(
                    standard.CreateTaskPushNotificationConfig,
                    (_, request, _) =>
                    {
                        captureRequest(request);
                        return ValueTask.FromResult(request);
                    });
                break;
            case ("GET", "/tasks/{id}/pushNotificationConfigs"):
                builder.Map(
                    standard.ListTaskPushNotificationConfigs,
                    (_, request, _) =>
                    {
                        captureRequest(request);
                        return ValueTask.FromResult(
                            new ListTaskPushNotificationConfigsResponse());
                    });
                break;
            case ("GET", "/tasks/{id}/pushNotificationConfigs/{configId}"):
                builder.Map(
                    standard.GetTaskPushNotificationConfig,
                    (_, request, _) =>
                    {
                        captureRequest(request);
                        return ValueTask.FromResult(
                            CreatePushNotificationConfig(request.TaskId));
                    });
                break;
            case ("DELETE", "/tasks/{id}/pushNotificationConfigs/{configId}"):
                builder.Map(
                    standard.DeleteTaskPushNotificationConfig,
                    (_, request, _) =>
                    {
                        captureRequest(request);
                        return ValueTask.FromResult(A2AEmptyResult.Instance);
                    });
                break;
            case ("GET", "/extendedAgentCard"):
                builder.Map(
                    standard.GetExtendedAgentCard,
                    (_, request, _) =>
                    {
                        captureRequest(request);
                        return ValueTask.FromResult(
                            new AgentCard
                            {
                                Name = "Test",
                                Description = "Test agent",
                            });
                    });
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown standard HTTP route '{httpMethod} {route}'.");
        }

        return builder.Build(operationCatalog);
    }

    private static void AssertExpectedRequest(
        string httpMethod,
        string route,
        object? capturedRequest)
    {
        switch ((httpMethod, route))
        {
            case ("GET", "/tasks/{id}"):
                var getTask = Assert.IsType<GetTaskRequest>(capturedRequest);
                Assert.Equal("task-1", getTask.Id);
                Assert.Equal(3, getTask.HistoryLength);
                break;
            case ("GET", "/tasks"):
                var listTasks = Assert.IsType<ListTasksRequest>(capturedRequest);
                Assert.Equal("context-1", listTasks.ContextId);
                Assert.Equal(TaskState.Working, listTasks.Status);
                Assert.Equal(25, listTasks.PageSize);
                Assert.Equal("next-page", listTasks.PageToken);
                Assert.Equal(2, listTasks.HistoryLength);
                break;
            case ("POST", "/tasks/{id}:cancel"):
                Assert.Equal(
                    "task-1",
                    Assert.IsType<CancelTaskRequest>(capturedRequest).Id);
                break;
            case ("POST", "/tasks/{id}:subscribe"):
                Assert.Equal(
                    "task-1",
                    Assert.IsType<SubscribeToTaskRequest>(capturedRequest).Id);
                break;
            case ("POST", "/message:send"):
            case ("POST", "/message:stream"):
                Assert.Equal(
                    "message-1",
                    Assert.IsType<SendMessageRequest>(capturedRequest)
                        .Message.MessageId);
                break;
            case ("POST", "/tasks/{id}/pushNotificationConfigs"):
                var createConfig =
                    Assert.IsType<TaskPushNotificationConfig>(capturedRequest);
                Assert.Equal("task-1", createConfig.TaskId);
                Assert.Null(createConfig.Tenant);
                Assert.Equal("config-1", createConfig.Id);
                break;
            case ("GET", "/tasks/{id}/pushNotificationConfigs"):
                var listConfigs =
                    Assert.IsType<ListTaskPushNotificationConfigsRequest>(
                        capturedRequest);
                Assert.Equal("task-1", listConfigs.TaskId);
                Assert.Equal(10, listConfigs.PageSize);
                Assert.Equal("config-page", listConfigs.PageToken);
                break;
            case ("GET", "/tasks/{id}/pushNotificationConfigs/{configId}"):
                var getConfig =
                    Assert.IsType<GetTaskPushNotificationConfigRequest>(
                        capturedRequest);
                Assert.Equal("task-1", getConfig.TaskId);
                Assert.Equal("config-1", getConfig.Id);
                break;
            case ("DELETE", "/tasks/{id}/pushNotificationConfigs/{configId}"):
                var deleteConfig =
                    Assert.IsType<DeleteTaskPushNotificationConfigRequest>(
                        capturedRequest);
                Assert.Equal("task-1", deleteConfig.TaskId);
                Assert.Equal("config-1", deleteConfig.Id);
                break;
            case ("GET", "/extendedAgentCard"):
                Assert.IsType<GetExtendedAgentCardRequest>(capturedRequest);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown standard HTTP route '{httpMethod} {route}'.");
        }
    }

    private static DefaultHttpContext CreateHttpContext(
        WebApplication app,
        string httpMethod,
        string route)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        httpContext.Request.Method = httpMethod;
        httpContext.Response.Body = new MemoryStream();

        switch ((httpMethod, route))
        {
            case ("GET", "/tasks/{id}"):
                httpContext.Request.RouteValues["id"] = "task-1";
                httpContext.Request.QueryString = new QueryString("?historyLength=3");
                break;
            case ("GET", "/tasks"):
                httpContext.Request.QueryString = new QueryString(
                    "?contextId=context-1&status=working&pageSize=25&pageToken=next-page&historyLength=2");
                break;
            case ("POST", "/tasks/{id}:cancel"):
            case ("POST", "/tasks/{id}:subscribe"):
                httpContext.Request.RouteValues["id"] = "task-1";
                break;
            case ("POST", "/message:send"):
            case ("POST", "/message:stream"):
                SetBody(
                    httpContext,
                    JsonSerializer.Serialize(
                        new SendMessageRequest
                        {
                            Message = new Message
                            {
                                MessageId = "message-1",
                                Role = Role.User,
                                Parts = [Part.FromText("hello")],
                            },
                        },
                        A2AJsonUtilities.DefaultOptions));
                break;
            case ("POST", "/tasks/{id}/pushNotificationConfigs"):
                httpContext.Request.RouteValues["id"] = "task-1";
                SetBody(
                    httpContext,
                    JsonSerializer.Serialize(
                        new TaskPushNotificationConfig
                        {
                            Id = "config-1",
                            TaskId = "body-task",
                            Tenant = "body-tenant",
                            Url = "https://example.com/callback",
                        },
                        A2AJsonUtilities.DefaultOptions));
                break;
            case ("GET", "/tasks/{id}/pushNotificationConfigs"):
                httpContext.Request.RouteValues["id"] = "task-1";
                httpContext.Request.QueryString =
                    new QueryString("?pageSize=10&pageToken=config-page");
                break;
            case ("GET", "/tasks/{id}/pushNotificationConfigs/{configId}"):
            case ("DELETE", "/tasks/{id}/pushNotificationConfigs/{configId}"):
                httpContext.Request.RouteValues["id"] = "task-1";
                httpContext.Request.RouteValues["configId"] = "config-1";
                break;
        }

        return httpContext;
    }

    private static RouteEndpoint GetEndpoint(
        WebApplication app,
        string httpMethod,
        string route) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint =>
                endpoint.RoutePattern.RawText == route
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!
                    .HttpMethods.Contains(httpMethod));

    private static void SetBody(DefaultHttpContext httpContext, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.ContentLength = bytes.Length;
        httpContext.Request.Body = new MemoryStream(bytes);
    }

    private static string GetResponseBody(DefaultHttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(
            httpContext.Response.Body,
            Encoding.UTF8,
            leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>
        GetTypeInfo<T>() =>
        (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)
            A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

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
        protected static InvalidOperationException CreateException() =>
            new("The request handler was invoked instead of the operation catalog.");

        public virtual Task<SendMessageResponse> SendMessageAsync(
            SendMessageRequest request,
            CancellationToken cancellationToken = default)
            => throw CreateException();

        public virtual IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(
            SendMessageRequest request,
            CancellationToken cancellationToken = default)
            => throw CreateException();

        public virtual Task<AgentTask> GetTaskAsync(
            GetTaskRequest request,
            CancellationToken cancellationToken = default)
            => throw CreateException();

        public virtual Task<ListTasksResponse> ListTasksAsync(
            ListTasksRequest request,
            CancellationToken cancellationToken = default)
            => throw CreateException();

        public virtual Task<AgentTask> CancelTaskAsync(
            CancelTaskRequest request,
            CancellationToken cancellationToken = default)
            => throw CreateException();

        public virtual IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(
            SubscribeToTaskRequest request,
            CancellationToken cancellationToken = default)
            => throw CreateException();

        public virtual Task<TaskPushNotificationConfig>
            CreateTaskPushNotificationConfigAsync(
                TaskPushNotificationConfig config,
                CancellationToken cancellationToken = default)
            => throw CreateException();

        public virtual Task<TaskPushNotificationConfig>
            GetTaskPushNotificationConfigAsync(
                GetTaskPushNotificationConfigRequest request,
                CancellationToken cancellationToken = default)
            => throw CreateException();

        public virtual Task<ListTaskPushNotificationConfigsResponse>
            ListTaskPushNotificationConfigsAsync(
                ListTaskPushNotificationConfigsRequest request,
                CancellationToken cancellationToken = default)
            => throw CreateException();

        public virtual Task DeleteTaskPushNotificationConfigAsync(
            DeleteTaskPushNotificationConfigRequest request,
            CancellationToken cancellationToken = default)
            => throw CreateException();

        public virtual Task<AgentCard> GetExtendedAgentCardAsync(
            GetExtendedAgentCardRequest request,
            CancellationToken cancellationToken = default)
            => throw CreateException();
    }

    private sealed class PushNotificationRequestHandler : ThrowingRequestHandler
    {
        internal int ProbeCallCount { get; private set; }

        internal int CreateCallCount { get; private set; }

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

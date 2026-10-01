using A2A.AspNetCore;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace A2A.UnitTests.AspNetCore;

public partial class A2AHttpCustomOperationTests
{
    [Fact]
    public async Task MapHttpA2A_StandardOperationUsesRequestSelectedHandler()
    {
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler(
                        request => Task.FromResult(
                            new AgentTask
                            {
                                Id = request.Id,
                                ContextId = "context-1",
                                Status = new TaskStatus
                                {
                                    State = TaskState.Working,
                                },
                            }))),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        app.MapHttpA2A(
            scopeFactory,
            new A2AOperationHandlerCatalogBuilder()
                .AddStandardA2AHandlers(standard)
                .Build(operationCatalog),
            new A2AHttpOperationBindingBuilder()
                .AddStandardA2AHttpBindings(standard)
                .Build());
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(static endpoint =>
                endpoint.RoutePattern.RawText == "/tasks/{id}");
        var httpContext = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.RouteValues["id"] = "task-1";
        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        await endpoint.RequestDelegate!(httpContext);

        Assert.Equal(1, disposeCount);
        responseBody.Position = 0;
        var task = await JsonSerializer.DeserializeAsync<AgentTask>(
            responseBody,
            A2AJsonUtilities.DefaultOptions);
        Assert.Equal("task-1", task!.Id);
    }

    [Fact]
    public void MapHttpA2A_CustomOnlyCatalogMapsOnlySuppliedRoute()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var customOperation = operationBuilder.DefineUnary<
            ResumeRequest,
            ResumeResult>(new A2AOperationId("test.compatibility"));
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                customOperation,
                static (_, request, _) => ValueTask.FromResult(
                    new ResumeResult(request.Token)))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:resumeAuth",
                customOperation,
                static (context, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)context.Request.RouteValues["id"]!,
                        "opaque-token")),
                CustomHttpJsonContext.Default.ResumeResult)
            .Build();
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler(
                        request => Task.FromResult(
                            new AgentTask
                            {
                                Id = request.Id,
                                ContextId = "context-1",
                                Status = new TaskStatus
                                {
                                    State = TaskState.Working,
                                },
                            })))));
        var app = WebApplication.CreateBuilder().Build();

        app.MapHttpA2A(scopeFactory, handlers, bindings);

        var endpoint = Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(static dataSource => dataSource.Endpoints)
                .OfType<RouteEndpoint>());
        Assert.Equal(
            "/tasks/{id}:resumeAuth",
            endpoint.RoutePattern.RawText);
        Assert.Contains(
            HttpMethods.Post,
            endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>()
                .HttpMethods);
    }

    [Fact]
    public async Task MapHttpA2A_PartialStandardAndCustomCatalogMapsOnlySuppliedRoutes()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var customOperation = operationBuilder.DefineUnary<
            ResumeRequest,
            ResumeResult>(new A2AOperationId("test.partial-compatibility"));
        var operationCatalog = operationBuilder.Build();
        var suppliedStandardDispatchCount = 0;
        var customDispatchCount = 0;
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                standard.ListTasks,
                (_, _, _) =>
                {
                    suppliedStandardDispatchCount++;
                    return ValueTask.FromResult(new ListTasksResponse());
                })
            .Map(
                customOperation,
                (_, request, _) =>
                {
                    customDispatchCount++;
                    return ValueTask.FromResult(
                        new ResumeResult(request.Token));
                })
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Get,
                "/tasks",
                standard.ListTasks,
                static (_, _) => ValueTask.FromResult(new ListTasksRequest()),
                (JsonTypeInfo<ListTasksResponse>)A2AJsonUtilities
                    .DefaultOptions.GetTypeInfo(typeof(ListTasksResponse)))
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:resumeAuth",
                customOperation,
                static (context, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)context.Request.RouteValues["id"]!,
                        "opaque-token")),
                CustomHttpJsonContext.Default.ResumeResult)
            .Build();
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler(
                        request => Task.FromResult(
                            new AgentTask
                            {
                                Id = request.Id,
                                ContextId = "synthesized-standard",
                                Status = new TaskStatus
                                {
                                    State = TaskState.Working,
                                },
                            })))));
        var app = WebApplication.CreateBuilder().Build();

        app.MapHttpA2A(scopeFactory, handlers, bindings);

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        Assert.Collection(
            endpoints.OrderBy(static endpoint => endpoint.RoutePattern.RawText),
            endpoint => Assert.Equal("/tasks", endpoint.RoutePattern.RawText),
            endpoint => Assert.Equal(
                "/tasks/{id}:resumeAuth",
                endpoint.RoutePattern.RawText));

        var standardContext = CreateHttpContext(app, "/tasks");
        standardContext.Request.Method = HttpMethods.Get;
        await GetEndpoint(
            app,
            HttpMethods.Get,
            "/tasks").RequestDelegate!(standardContext);
        var customContext = CreateHttpContext(app, "/tasks/{id}:resumeAuth");
        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/tasks/{id}:resumeAuth").RequestDelegate!(customContext);

        Assert.Equal(1, suppliedStandardDispatchCount);
        Assert.Equal(1, customDispatchCount);
    }

    [Fact]
    public async Task MapHttpA2A_CustomOperationBindsRouteAndBodyAndDisposesRequestScope()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<ResumeRequest, ResumeResult>(
            new A2AOperationId("test.resumeAuth"));
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                operation,
                static (_, request, _) => ValueTask.FromResult(
                    new ResumeResult($"{request.TaskId}:{request.Token}")))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:resumeAuth",
                operation,
                static async (httpContext, cancellationToken) =>
                {
                    var body = await JsonSerializer.DeserializeAsync(
                        httpContext.Request.Body,
                        CustomHttpJsonContext.Default.ResumeBody,
                        cancellationToken);
                    return new ResumeRequest(
                        (string)httpContext.Request.RouteValues["id"]!,
                        body!.Token);
                },
                CustomHttpJsonContext.Default.ResumeResult)
            .Build();
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler()),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(static endpoint =>
                endpoint.RoutePattern.RawText == "/tasks/{id}:resumeAuth");
        var httpContext = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.RouteValues["id"] = "task-1";
        httpContext.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes("""{"token":"opaque-token"}"""));
        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        await endpoint.RequestDelegate!(httpContext);

        Assert.Equal(1, disposeCount);
        responseBody.Position = 0;
        var result = await JsonSerializer.DeserializeAsync(
            responseBody,
            CustomHttpJsonContext.Default.ResumeResult);
        Assert.Equal("task-1:opaque-token", result!.Value);
    }

    [Fact]
    public async Task MapHttpA2A_CustomOperationMapsA2AExceptionAndDisposesRequestScope()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<ResumeRequest, ResumeResult>(
            new A2AOperationId("test.resumeAuth"));
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map<ResumeRequest, ResumeResult>(
                operation,
                static (_, _, _) => throw new A2AException(
                    "Invalid resume request.",
                    A2AErrorCode.InvalidParams))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:resumeAuth",
                operation,
                static (httpContext, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)httpContext.Request.RouteValues["id"]!,
                        "opaque-token")),
                CustomHttpJsonContext.Default.ResumeResult)
            .Build();
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler()),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(static endpoint =>
                endpoint.RoutePattern.RawText == "/tasks/{id}:resumeAuth");
        var httpContext = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.RouteValues["id"] = "task-1";
        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        await endpoint.RequestDelegate!(httpContext);

        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Equal(1, disposeCount);
    }

    [Fact]
    public async Task MapHttpA2A_CustomBindingFailureDoesNotCreateRequestScope()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<ResumeRequest, ResumeResult>(
            new A2AOperationId("test.binding-failure"));
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                operation,
                static (_, request, _) =>
                    ValueTask.FromResult(new ResumeResult(request.Token)))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:bindingFailure",
                operation,
                static (_, _) => throw new A2AException(
                    "The HTTP request is invalid.",
                    A2AErrorCode.InvalidParams),
                CustomHttpJsonContext.Default.ResumeResult)
            .Build();
        var scopeCreateCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(
                        new A2AJsonRpcCustomOperationTests.TestRequestHandler())));
        };
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, "/tasks/{id}:bindingFailure");

        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/tasks/{id}:bindingFailure").RequestDelegate!(httpContext);

        Assert.Equal(0, scopeCreateCount);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
    }

    [Fact]
    public void A2AHttpBindingException_NullResultIsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => new A2AHttpBindingException(null!));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("text/plain", false)]
    [InlineData("application/json", true)]
    public async Task MapHttpA2A_CustomBindingContentTypeValidationOccursBeforeScopeCreation(
        string? contentType,
        bool shouldDispatch)
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<ResumeRequest, ResumeResult>(
            new A2AOperationId("test.content-type-binding"));
        var operationCatalog = operationBuilder.Build();
        var handlerCallCount = 0;
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                operation,
                (_, request, _) =>
                {
                    handlerCallCount++;
                    return ValueTask.FromResult(new ResumeResult(request.Token));
                })
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:contentType",
                operation,
                static (httpContext, _) =>
                {
                    if (!httpContext.Request.HasJsonContentType())
                    {
                        throw new A2AHttpBindingException(
                            Results.StatusCode(
                                StatusCodes.Status415UnsupportedMediaType));
                    }

                    return ValueTask.FromResult(
                        new ResumeRequest(
                            (string)httpContext.Request.RouteValues["id"]!,
                            "opaque-token"));
                },
                CustomHttpJsonContext.Default.ResumeResult)
            .Build();
        var scopeCreateCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(
                        new A2AJsonRpcCustomOperationTests.TestRequestHandler())));
        };
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, "/tasks/{id}:contentType");
        httpContext.Request.ContentType = contentType;

        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/tasks/{id}:contentType").RequestDelegate!(httpContext);

        var expectedDispatchCount = shouldDispatch ? 1 : 0;
        Assert.Equal(expectedDispatchCount, handlerCallCount);
        Assert.Equal(expectedDispatchCount, scopeCreateCount);
        Assert.Equal(
            shouldDispatch
                ? StatusCodes.Status200OK
                : StatusCodes.Status415UnsupportedMediaType,
            httpContext.Response.StatusCode);
        if (shouldDispatch)
        {
            Assert.Contains(
                "opaque-token",
                GetResponseBody(httpContext),
                StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(httpContext.Response.ContentType);
            Assert.Empty(GetResponseBody(httpContext));
        }
    }

    [Fact]
    public async Task MapHttpA2A_CustomValidatorRunsBeforeRequestScopeCreation()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<ResumeRequest, ResumeResult>(
            new A2AOperationId("test.validation"),
            _ => throw new A2AException(
                "The resume request is invalid.",
                A2AErrorCode.InvalidParams));
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                operation,
                static (_, request, _) =>
                    ValueTask.FromResult(new ResumeResult(request.Token)))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:validate",
                operation,
                static (context, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)context.Request.RouteValues["id"]!,
                        "opaque-token")),
                CustomHttpJsonContext.Default.ResumeResult)
            .Build();
        var scopeCreateCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(
                        new A2AJsonRpcCustomOperationTests.TestRequestHandler())));
        };
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, "/tasks/{id}:validate");

        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/tasks/{id}:validate").RequestDelegate!(httpContext);

        Assert.Equal(0, scopeCreateCount);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Contains(
            "resume request is invalid",
            GetResponseBody(httpContext),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MapHttpA2A_CustomValidatorCannotReturnRawBindingFailure()
    {
        var expectedException = new A2AHttpBindingException(
            Results.StatusCode(StatusCodes.Status409Conflict));
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<ResumeRequest, ResumeResult>(
            new A2AOperationId("test.validation-binding-failure"),
            _ => throw expectedException);
        var operationCatalog = operationBuilder.Build();
        var handlerCallCount = 0;
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                operation,
                (_, request, _) =>
                {
                    handlerCallCount++;
                    return ValueTask.FromResult(new ResumeResult(request.Token));
                })
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:validationBindingFailure",
                operation,
                static (context, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)context.Request.RouteValues["id"]!,
                        "opaque-token")),
                CustomHttpJsonContext.Default.ResumeResult)
            .Build();
        var scopeCreateCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) =>
        {
            scopeCreateCount++;
            return ValueTask.FromResult(
                new A2ARequestScope(
                    new A2AOperationContext(
                        new A2AJsonRpcCustomOperationTests.TestRequestHandler())));
        };
        var loggerProvider = new CapturingLoggerProvider();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.AddProvider(loggerProvider);
        var app = builder.Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(
            app,
            "/tasks/{id}:validationBindingFailure");

        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/tasks/{id}:validationBindingFailure").RequestDelegate!(httpContext);

        Assert.Equal(0, handlerCallCount);
        Assert.Equal(0, scopeCreateCount);
        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            httpContext.Response.StatusCode);
        Assert.Contains(
            loggerProvider.Entries,
            entry =>
                entry.LogLevel == LogLevel.Error
                && ReferenceEquals(entry.Exception, expectedException));
    }

    [Fact]
    public async Task MapHttpA2A_CustomUnaryScopeSurvivesResponseSerialization()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<ResumeRequest, ScopeAwareResult>(
            new A2AOperationId("test.scope-aware-result"));
        var operationCatalog = operationBuilder.Build();
        var disposeCount = 0;
        ScopeAwareResult? operationResult = null;
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                operation,
                (_, _, _) =>
                {
                    operationResult = new ScopeAwareResult
                    {
                        IsScopeDisposed = () => disposeCount != 0,
                    };
                    return ValueTask.FromResult(operationResult);
                })
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:scopeAware",
                operation,
                static (context, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)context.Request.RouteValues["id"]!,
                        "opaque-token")),
                CustomHttpJsonContext.Default.ScopeAwareResult)
            .Build();
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler()),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, "/tasks/{id}:scopeAware");

        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/tasks/{id}:scopeAware").RequestDelegate!(httpContext);

        Assert.NotNull(operationResult);
        Assert.True(operationResult.ScopeWasActiveDuringSerialization);
        Assert.Equal(1, disposeCount);
    }

    [Fact]
    public async Task MapHttpA2A_DeclaredOperationErrorUsesConfiguredStatusAndDetailsMetadata()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<ResumeRequest, ResumeResult>(
            new A2AOperationId("test.declared-error"));
        var declaredError = operationBuilder.DeclareError<
            ResumeRequest,
            ResumeResult,
            CustomErrorDetails>(
                operation,
                "https://example.com/errors/authorization-request-not-found");
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map<ResumeRequest, ResumeResult>(
                operation,
                (_, _, _) => throw new A2AOperationException<CustomErrorDetails>(
                    declaredError,
                    "The authorization request was not found.",
                    new CustomErrorDetails("authorization-1")))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:declaredError",
                operation,
                static (context, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)context.Request.RouteValues["id"]!,
                        "opaque-token")),
                CustomHttpJsonContext.Default.ResumeResult)
            .MapError(
                operation,
                declaredError,
                StatusCodes.Status404NotFound,
                CustomHttpJsonContext.Default.CustomErrorDetails)
            .Build();
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler()),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, "/tasks/{id}:declaredError");

        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/tasks/{id}:declaredError").RequestDelegate!(httpContext);

        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
        Assert.Equal(1, disposeCount);
        using var response = JsonDocument.Parse(GetResponseBody(httpContext));
        var error = response.RootElement.GetProperty("error");
        Assert.Equal(
            "The authorization request was not found.",
            error.GetProperty("message").GetString());
        Assert.Equal(
            declaredError.ErrorId,
            error.GetProperty("details")[0].GetProperty("@type").GetString());
        Assert.Equal(
            "authorization-1",
            error.GetProperty("details")[0]
                .GetProperty("authorizationRequestId").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapHttpA2A_UnmappedOrUnserializableOperationErrorReturnsGenericInternalError(
        bool mapErrorWithFailingDetails)
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<ResumeRequest, ResumeResult>(
            new A2AOperationId("test.internal-error"));
        var declaredError = operationBuilder.DeclareError<
            ResumeRequest,
            ResumeResult,
            ThrowingErrorDetails>(
                operation,
                "https://example.com/errors/internal");
        var operationCatalog = operationBuilder.Build();
        const string secretMessage = "secret handler failure";
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map<ResumeRequest, ResumeResult>(
                operation,
                (_, _, _) => throw new A2AOperationException<ThrowingErrorDetails>(
                    declaredError,
                    secretMessage,
                    new ThrowingErrorDetails()))
            .Build(operationCatalog);
        var bindingBuilder = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:internalError",
                operation,
                static (context, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)context.Request.RouteValues["id"]!,
                        "opaque-token")),
                CustomHttpJsonContext.Default.ResumeResult);
        if (mapErrorWithFailingDetails)
        {
            bindingBuilder.MapError(
                operation,
                declaredError,
                StatusCodes.Status409Conflict,
                CustomHttpJsonContext.Default.ThrowingErrorDetails);
        }

        var bindings = bindingBuilder.Build();
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler())));
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, "/tasks/{id}:internalError");

        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/tasks/{id}:internalError").RequestDelegate!(httpContext);

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            httpContext.Response.StatusCode);
        var responseText = GetResponseBody(httpContext);
        Assert.Contains(
            "An internal error occurred.",
            responseText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(secretMessage, responseText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            ThrowingErrorDetails.FailureMessage,
            responseText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapHttpA2A_ResultSerializationFailureReturnsGenericInternalErrorAndDisposesScope()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<ResumeRequest, ThrowingResult>(
            new A2AOperationId("test.serialization-error"));
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                operation,
                static (_, _, _) => ValueTask.FromResult(new ThrowingResult()))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:serializationError",
                operation,
                static (context, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)context.Request.RouteValues["id"]!,
                        "opaque-token")),
                CustomHttpJsonContext.Default.ThrowingResult)
            .Build();
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler()),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(
            app,
            "/tasks/{id}:serializationError");

        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/tasks/{id}:serializationError").RequestDelegate!(httpContext);

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            httpContext.Response.StatusCode);
        Assert.Equal(1, disposeCount);
        var responseText = GetResponseBody(httpContext);
        Assert.Contains(
            "An internal error occurred.",
            responseText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ThrowingResult.FailureMessage,
            responseText,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task MapHttpA2A_UnexpectedDispatchExceptionIsLogged(
        bool throwFromScopeFactory,
        bool throwBindingException)
    {
        Exception expectedException = throwBindingException
            ? new A2AHttpBindingException(
                Results.StatusCode(StatusCodes.Status409Conflict))
            : new InvalidOperationException(
                throwFromScopeFactory
                    ? "secret scope factory failure"
                    : "secret handler failure");
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<ResumeRequest, ResumeResult>(
            new A2AOperationId("test.logging"));
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map<ResumeRequest, ResumeResult>(
                operation,
                (_, _, _) => throw expectedException)
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:logging",
                operation,
                static (context, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)context.Request.RouteValues["id"]!,
                        "opaque-token")),
                CustomHttpJsonContext.Default.ResumeResult)
            .Build();
        A2ARequestScopeFactory scopeFactory = (_, _) =>
            throwFromScopeFactory
                ? throw expectedException
                : ValueTask.FromResult(
                    new A2ARequestScope(
                        new A2AOperationContext(
                            new A2AJsonRpcCustomOperationTests.TestRequestHandler())));
        var loggerProvider = new CapturingLoggerProvider();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.AddProvider(loggerProvider);
        var app = builder.Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, "/tasks/{id}:logging");

        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/tasks/{id}:logging").RequestDelegate!(httpContext);

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            httpContext.Response.StatusCode);
        Assert.Contains(
            loggerProvider.Entries,
            entry =>
                entry.LogLevel == LogLevel.Error
                && ReferenceEquals(entry.Exception, expectedException)
                && entry.Message.Contains(
                    "Unexpected error",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            expectedException.Message,
            GetResponseBody(httpContext),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_MapErrorAfterBuildDoesNotMutatePriorBindings()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<ResumeRequest, ResumeResult>(
            new A2AOperationId("test.immutable-bindings"));
        var declaredError = operationBuilder.DeclareError<
            ResumeRequest,
            ResumeResult,
            CustomErrorDetails>(
                operation,
                "https://example.com/errors/immutable");
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map<ResumeRequest, ResumeResult>(
                operation,
                (_, _, _) => throw new A2AOperationException<CustomErrorDetails>(
                    declaredError,
                    "The immutable binding failed.",
                    new CustomErrorDetails("authorization-1")))
            .Build(operationCatalog);
        var bindingBuilder = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:immutable",
                operation,
                static (context, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)context.Request.RouteValues["id"]!,
                        "opaque-token")),
                CustomHttpJsonContext.Default.ResumeResult);
        var priorBindings = bindingBuilder.Build();
        bindingBuilder.MapError(
            operation,
            declaredError,
            StatusCodes.Status409Conflict,
            CustomHttpJsonContext.Default.CustomErrorDetails);
        var updatedBindings = bindingBuilder.Build();
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler())));
        var priorApp = WebApplication.CreateBuilder().Build();
        priorApp.MapHttpA2A(scopeFactory, handlers, priorBindings);
        var updatedApp = WebApplication.CreateBuilder().Build();
        updatedApp.MapHttpA2A(scopeFactory, handlers, updatedBindings);
        var priorContext = CreateHttpContext(priorApp, "/tasks/{id}:immutable");
        var updatedContext = CreateHttpContext(updatedApp, "/tasks/{id}:immutable");

        await GetEndpoint(
            priorApp,
            HttpMethods.Post,
            "/tasks/{id}:immutable").RequestDelegate!(priorContext);
        await GetEndpoint(
            updatedApp,
            HttpMethods.Post,
            "/tasks/{id}:immutable").RequestDelegate!(updatedContext);

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            priorContext.Response.StatusCode);
        Assert.Equal(
            StatusCodes.Status409Conflict,
            updatedContext.Response.StatusCode);
    }

    [Fact]
    public async Task MapHttpA2A_CustomStreamingScopeSurvivesSseCompletion()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineStreaming<ResumeRequest, CustomStreamEvent>(
            new A2AOperationId("test.stream"));
        var operationCatalog = operationBuilder.Build();
        var disposeCount = 0;
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .MapStreaming(
                operation,
                (_, request, cancellationToken) =>
                    YieldStreamEventAsync(
                        new CustomStreamEvent(request.Token),
                        () => disposeCount != 0,
                        cancellationToken))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .MapStreaming(
                HttpMethods.Post,
                "/tasks/{id}:stream",
                operation,
                static (context, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)context.Request.RouteValues["id"]!,
                        "event-1")),
                CustomHttpJsonContext.Default.CustomStreamEvent)
            .Build();
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler()),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, "/tasks/{id}:stream");

        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/tasks/{id}:stream").RequestDelegate!(httpContext);

        Assert.Equal(1, disposeCount);
        Assert.Equal("text/event-stream", httpContext.Response.ContentType);
        Assert.Contains(
            "\"value\":\"event-1\"",
            GetResponseBody(httpContext),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapHttpA2A_CustomStreamingDisposesScopeAfterEmptyStream()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineStreaming<ResumeRequest, CustomStreamEvent>(
            new A2AOperationId("test.empty-stream"));
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .MapStreaming(
                operation,
                static (_, _, cancellationToken) =>
                    EmptyStreamAsync(cancellationToken))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .MapStreaming(
                HttpMethods.Post,
                "/tasks/{id}:emptyStream",
                operation,
                static (context, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)context.Request.RouteValues["id"]!,
                        "unused")),
                CustomHttpJsonContext.Default.CustomStreamEvent)
            .Build();
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler()),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, "/tasks/{id}:emptyStream");

        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/tasks/{id}:emptyStream").RequestDelegate!(httpContext);

        Assert.Equal(1, disposeCount);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal("text/event-stream", httpContext.Response.ContentType);
        Assert.Empty(GetResponseBody(httpContext));
    }

    [Fact]
    public async Task MapHttpA2A_CustomStreamingDeclaredErrorBeforeFirstEventUsesConfiguredStatus()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineStreaming<ResumeRequest, CustomStreamEvent>(
            new A2AOperationId("test.stream-error"));
        var declaredError = operationBuilder.DeclareError<
            ResumeRequest,
            CustomStreamEvent,
            CustomErrorDetails>(
                operation,
                "https://example.com/errors/stream");
        var operationCatalog = operationBuilder.Build();
        var operationException = new A2AOperationException<CustomErrorDetails>(
            declaredError,
            "The stream could not start.",
            new CustomErrorDetails("stream-authorization"));
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .MapStreaming(
                operation,
                (_, _, _) => ThrowBeforeFirstEventAsync(operationException))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .MapStreaming(
                HttpMethods.Post,
                "/tasks/{id}:streamError",
                operation,
                static (context, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)context.Request.RouteValues["id"]!,
                        "unused")),
                CustomHttpJsonContext.Default.CustomStreamEvent)
            .MapError(
                operation,
                declaredError,
                StatusCodes.Status409Conflict,
                CustomHttpJsonContext.Default.CustomErrorDetails)
            .Build();
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler()),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(app, "/tasks/{id}:streamError");

        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/tasks/{id}:streamError").RequestDelegate!(httpContext);

        Assert.Equal(1, disposeCount);
        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.Equal("application/json", httpContext.Response.ContentType);
        Assert.Contains(
            "stream-authorization",
            GetResponseBody(httpContext),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapHttpA2A_CustomStreamingCancellationDisposesEnumeratorAndScope()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineStreaming<ResumeRequest, CustomStreamEvent>(
            new A2AOperationId("test.cancelled-stream"));
        var operationCatalog = operationBuilder.Build();
        var enumeratorDisposeCount = 0;
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .MapStreaming(
                operation,
                (_, _, cancellationToken) =>
                    CancelledStreamAsync(
                        () => enumeratorDisposeCount++,
                        cancellationToken))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .MapStreaming(
                HttpMethods.Post,
                "/tasks/{id}:cancelledStream",
                operation,
                static (context, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)context.Request.RouteValues["id"]!,
                        "unused")),
                CustomHttpJsonContext.Default.CustomStreamEvent)
            .Build();
        var scopeDisposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler()),
                () =>
                {
                    scopeDisposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var app = WebApplication.CreateBuilder().Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var httpContext = CreateHttpContext(
            app,
            "/tasks/{id}:cancelledStream");
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        httpContext.RequestAborted = cancellationSource.Token;

        await GetEndpoint(
            app,
            HttpMethods.Post,
            "/tasks/{id}:cancelledStream").RequestDelegate!(httpContext);

        Assert.Equal(1, enumeratorDisposeCount);
        Assert.Equal(1, scopeDisposeCount);
        Assert.Null(httpContext.Response.ContentType);
        Assert.Empty(GetResponseBody(httpContext));
    }

    [Fact]
    public void MapHttpA2A_WhenErrorMappingUsesAnotherOperationsError_Throws()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<ResumeRequest, ResumeResult>(
            new A2AOperationId("test.error-owner"));
        var otherOperation = operationBuilder.DefineUnary<ResumeRequest, ResumeResult>(
            new A2AOperationId("test.other-error-owner"));
        var otherError = operationBuilder.DeclareError<
            ResumeRequest,
            ResumeResult,
            CustomErrorDetails>(
                otherOperation,
                "https://example.com/errors/wrong-owner");
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                operation,
                static (_, request, _) =>
                    ValueTask.FromResult(new ResumeResult(request.Token)))
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:wrongErrorOwner",
                operation,
                static (context, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)context.Request.RouteValues["id"]!,
                        "opaque-token")),
                CustomHttpJsonContext.Default.ResumeResult)
            .MapError(
                operation,
                otherError,
                StatusCodes.Status409Conflict,
                CustomHttpJsonContext.Default.CustomErrorDetails)
            .Build();
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler())));
        var app = WebApplication.CreateBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => app.MapHttpA2A(scopeFactory, handlers, bindings));

        Assert.Contains(
            otherError.ErrorId,
            exception.Message,
            StringComparison.Ordinal);
    }

    private static DefaultHttpContext CreateHttpContext(
        WebApplication app,
        string route)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.RouteValues["id"] = "task-1";
        httpContext.Request.Path = route.Replace("{id}", "task-1");
        httpContext.Response.Body = new MemoryStream();
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

    private static string GetResponseBody(DefaultHttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(
            httpContext.Response.Body,
            Encoding.UTF8,
            leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static async IAsyncEnumerable<CustomStreamEvent> YieldStreamEventAsync(
        CustomStreamEvent streamEvent,
        Func<bool> isScopeDisposed,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.False(isScopeDisposed());
        yield return streamEvent;
        await Task.Yield();
        Assert.False(isScopeDisposed());
    }

    private static async IAsyncEnumerable<CustomStreamEvent> EmptyStreamAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<CustomStreamEvent>
        ThrowBeforeFirstEventAsync(Exception exception)
    {
        await Task.CompletedTask;
        throw exception;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<CustomStreamEvent> CancelledStreamAsync(
        Action onDispose,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            onDispose();
        }

        yield break;
    }

    internal sealed record ResumeBody(
        [property: JsonPropertyName("token")] string Token);

    internal sealed record ResumeRequest(string TaskId, string Token);

    internal sealed record ResumeResult(
        [property: JsonPropertyName("value")] string Value);

    internal sealed record CustomStreamEvent(
        [property: JsonPropertyName("value")] string Value);

    internal sealed record CustomErrorDetails(
        [property: JsonPropertyName("authorizationRequestId")]
        string AuthorizationRequestId);

    internal sealed class ScopeAwareResult
    {
        [JsonIgnore]
        internal Func<bool> IsScopeDisposed { get; init; } = static () => true;

        [JsonIgnore]
        internal bool ScopeWasActiveDuringSerialization { get; private set; }

        [JsonPropertyName("value")]
        public string Value
        {
            get
            {
                ScopeWasActiveDuringSerialization = !IsScopeDisposed();
                return "serialized";
            }
        }
    }

    internal sealed class ThrowingResult
    {
        internal const string FailureMessage = "secret result serialization failure";
        private readonly string _failureMessage = FailureMessage;

        [JsonPropertyName("value")]
        public string Value => throw new InvalidOperationException(_failureMessage);
    }

    internal sealed class ThrowingErrorDetails
    {
        internal const string FailureMessage = "secret error serialization failure";
        private readonly string _failureMessage = FailureMessage;

        [JsonPropertyName("value")]
        public string Value => throw new InvalidOperationException(_failureMessage);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        internal List<CapturedLogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) =>
            new CapturingLogger(Entries);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(List<CapturedLogEntry> entries)
        : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Add(
                new CapturedLogEntry(
                    logLevel,
                    exception,
                    formatter(state, exception)));
        }
    }

    private sealed record CapturedLogEntry(
        LogLevel LogLevel,
        Exception? Exception,
        string Message);

    [JsonSerializable(typeof(ResumeBody))]
    [JsonSerializable(typeof(ResumeResult))]
    [JsonSerializable(typeof(CustomStreamEvent))]
    [JsonSerializable(typeof(CustomErrorDetails))]
    [JsonSerializable(typeof(ScopeAwareResult))]
    [JsonSerializable(typeof(ThrowingResult))]
    [JsonSerializable(typeof(ThrowingErrorDetails))]
    internal sealed partial class CustomHttpJsonContext : JsonSerializerContext;
}

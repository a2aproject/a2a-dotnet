using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace A2A.AspNetCore.Tests;

public class A2AEndpointRouteBuilderExtensionsTests
{
    [Fact]
    public async Task MapA2A_ListTasks_BindsTimestampAndArtifactFilters()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolver = A2AJsonUtilities.DefaultOptions.TypeInfoResolver);
        await using var app = builder.Build();
        var requestHandler = new Mock<IA2ARequestHandler>(MockBehavior.Strict);
        var statusTimestampAfter = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        requestHandler.Setup(handler => handler.ListTasksAsync(
                It.Is<ListTasksRequest>(request =>
                    request.StatusTimestampAfter == statusTimestampAfter && request.IncludeArtifacts == true),
                It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListTasksResponse { Tasks = [], NextPageToken = "" });
        app.MapHttpA2A(requestHandler.Object);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/tasks" &&
                candidate.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));
        using var responseBody = new MemoryStream();
        using var scope = app.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = "GET";
        context.Request.Path = "/tasks";
        context.Request.QueryString = new QueryString(
            $"?statusTimestampAfter={Uri.EscapeDataString(statusTimestampAfter.ToString("O"))}&includeArtifacts=true");
        context.Response.Body = responseBody;

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("application/a2a+json", context.Response.ContentType);
        requestHandler.VerifyAll();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("body-tenant")]
    public async Task MapHttpA2A_CreatePushConfig_UsesRouteTaskIdAndClearsBodyTenant(string? tenant)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolver = A2AJsonUtilities.DefaultOptions.TypeInfoResolver);
        await using var app = builder.Build();
        var requestHandler = new Mock<IA2ARequestHandler>(MockBehavior.Strict);
        requestHandler.Setup(handler => handler.CreateTaskPushNotificationConfigAsync(
                It.Is<TaskPushNotificationConfig>(config =>
                    config.TaskId == "route-task" && config.Tenant == null &&
                    config.Id == "cfg-1" && config.Url == "http://callback" &&
                    config.Token == "callback-token" &&
                    config.Authentication != null && config.Authentication.Scheme == "bearer"),
                It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskPushNotificationConfig
            {
                Id = "cfg-1", TaskId = "route-task", Url = "http://callback"
            });
        app.MapHttpA2A(requestHandler.Object);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/tasks/{id}/pushNotificationConfigs" &&
                candidate.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST"));
        var body = System.Text.Json.JsonSerializer.Serialize(new TaskPushNotificationConfig
        {
            Id = "cfg-1",
            TaskId = "body-task",
            Tenant = tenant,
            Url = "http://callback",
            Token = "callback-token",
            Authentication = new AuthenticationInfo { Scheme = "bearer" }
        }, A2AJsonUtilities.DefaultOptions);
        using var requestBody = new MemoryStream(Encoding.UTF8.GetBytes(body));
        using var responseBody = new MemoryStream();
        using var scope = app.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = "POST";
        context.Request.Path = "/tasks/route-task/pushNotificationConfigs";
        context.Request.RouteValues["id"] = "route-task";
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = requestBody.Length;
        context.Request.Body = requestBody;
        context.Response.Body = responseBody;
        context.Features.Set(Mock.Of<IHttpRequestBodyDetectionFeature>(feature => feature.CanHaveBody));

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        requestHandler.VerifyAll();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MapHttpA2A_CancelTask_PreservesBodyMetadata(bool includeContentLength)
    {
        var result = await ExecuteCancelTaskEndpointAsync(
            """{"metadata":{"reason":"user","details":{"retry":false}}}""",
            includeContentLength);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Context);
        Assert.NotNull(result.Context.Metadata);
        Assert.Equal("user", result.Context.Metadata["reason"].GetString());
        Assert.False(result.Context.Metadata["details"].GetProperty("retry").GetBoolean());
    }

    [Theory]
    [InlineData("""{"Metadata":{"reason":"user"}}""")]
    [InlineData("""{"mEtAdAtA":{"reason":"user"},"TeNaNt":"body-tenant"}""")]
    [InlineData("""{"metadata":{"reason":"earlier"},"Metadata":{"reason":"user"}}""")]
    public async Task MapHttpA2A_CancelTask_MatchesBodyPropertiesCaseInsensitively(string body)
    {
        var result = await ExecuteCancelTaskEndpointAsync(body);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Request);
        Assert.Null(result.Request.Tenant);
        Assert.NotNull(result.Context);
        Assert.NotNull(result.Context.Metadata);
        Assert.Equal("user", result.Context.Metadata["reason"].GetString());
    }

    [Fact]
    public async Task MapHttpA2A_CancelTask_UsesRouteIdAndClearsBodyTenant()
    {
        var result = await ExecuteCancelTaskEndpointAsync(
            """{"id":"body-task","tenant":"body-tenant","metadata":{"reason":"user"}}""");

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Request);
        Assert.Equal("route-task", result.Request.Id);
        Assert.Null(result.Request.Tenant);
        Assert.NotNull(result.Context);
        Assert.Equal("route-task", result.Context.TaskId);
        Assert.NotNull(result.Context.Metadata);
        Assert.Equal("user", result.Context.Metadata["reason"].GetString());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("{}", true)]
    [InlineData("{\"metadata\":null}", true)]
    public async Task MapHttpA2A_CancelTask_AcceptsPlainCancellation(string? body, bool includeContentLength)
    {
        var result = await ExecuteCancelTaskEndpointAsync(body, includeContentLength);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Context);
        Assert.Equal("route-task", result.Context.TaskId);
        Assert.Null(result.Context.Metadata);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"metadata\":42}")]
    [InlineData("{\"metadata\":[]}")]
    [InlineData("{\"tenant\":42}")]
    [InlineData("{\"Metadata\":42}")]
    [InlineData("{\"TENANT\":42}")]
    public async Task MapHttpA2A_CancelTask_InvalidBodyDoesNotCancelTask(string body)
    {
        var result = await ExecuteCancelTaskEndpointAsync(body);

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Null(result.Request);
        Assert.Null(result.Context);
    }

    [Fact]
    public void MapA2A_RegistersEndpoint_WithCorrectPath()
    {
        // Arrange
        var app = WebApplication.CreateBuilder().Build();
        var requestHandler = new Mock<IA2ARequestHandler>().Object;

        // Act & Assert - Should not throw
        var result = app.MapA2A(requestHandler, "/agent");
        Assert.NotNull(result);
    }

    [Fact]
    public void MapWellKnownAgentCard_RegistersEndpoint()
    {
        // Arrange
        var app = WebApplication.CreateBuilder().Build();
        var agentCard = new AgentCard { Name = "Test", Description = "Test agent" };

        // Act & Assert - Should not throw
        var result = app.MapWellKnownAgentCard(agentCard);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task MapWellKnownAgentCard_ResponseIncludesCacheControlMaxAge()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Add(A2AJsonUtilities.DefaultOptions.TypeInfoResolver!));
        var app = builder.Build();
        var agentCard = new AgentCard { Name = "Test", Description = "Test agent" };
        app.MapWellKnownAgentCard(agentCard);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single();
        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        context.Response.Body = new MemoryStream();

        // Act
        await endpoint.RequestDelegate!(context);

        // Assert
        Assert.Equal("public, max-age=3600", context.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task MapWellKnownAgentCard_WithCacheOptions_UsesConfiguredMaxAge()
    {
        var response = await ExecuteAgentCardEndpointAsync(
            new AgentCard { Name = "Test", Description = "Test agent" },
            new AgentCardCacheOptions { MaxAge = TimeSpan.FromMinutes(15) });

        Assert.Equal("public, max-age=900", response.Headers.CacheControl);
    }

    [Fact]
    public void MapWellKnownAgentCard_WithNegativeMaxAge_Throws()
    {
        var app = WebApplication.CreateBuilder().Build();
        var agentCard = new AgentCard { Name = "Test", Description = "Test agent" };

        Assert.Throws<ArgumentOutOfRangeException>(() => app.MapWellKnownAgentCard(
            agentCard,
            cacheOptions: new AgentCardCacheOptions { MaxAge = TimeSpan.FromSeconds(-1) }));
    }

    [Fact]
    public async Task MapWellKnownAgentCard_ResponseIncludesBodyDerivedETag()
    {
        var firstResponse = await ExecuteAgentCardEndpointAsync(
            new AgentCard { Name = "First", Description = "Test agent" });
        var secondResponse = await ExecuteAgentCardEndpointAsync(
            new AgentCard { Name = "Second", Description = "Test agent" });

        Assert.False(string.IsNullOrEmpty(firstResponse.Headers.ETag));
        Assert.NotEqual(firstResponse.Headers.ETag, secondResponse.Headers.ETag);
        Assert.Equal(
            $"\"{Convert.ToHexString(SHA256.HashData(((MemoryStream)firstResponse.Body).ToArray()))}\"",
            firstResponse.Headers.ETag);
    }

    [Fact]
    public async Task MapWellKnownAgentCard_ResponseIncludesLastModified()
    {
        var response = await ExecuteAgentCardEndpointAsync(
            new AgentCard { Name = "Test", Description = "Test agent" });

        Assert.True(DateTimeOffset.TryParseExact(
            response.Headers.LastModified,
            "R",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out _));
    }

    [Fact]
    public void MapA2A_And_MapWellKnownAgentCard_Together_RegistersBothEndpoints()
    {
        // Arrange
        var app = WebApplication.CreateBuilder().Build();
        var requestHandler = new Mock<IA2ARequestHandler>().Object;
        var agentCard = new AgentCard { Name = "Test", Description = "Test agent" };

        // Act & Assert - Should not throw when calling both
        var result1 = app.MapA2A(requestHandler, "/agent");
        var result2 = app.MapWellKnownAgentCard(agentCard);

        Assert.NotNull(result1);
        Assert.NotNull(result2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MapA2A_ThrowsArgumentException_WhenPathIsNullOrEmpty(string? path)
    {
        // Arrange
        var app = WebApplication.CreateBuilder().Build();
        var requestHandler = new Mock<IA2ARequestHandler>().Object;

        // Act & Assert
        if (path == null)
        {
            Assert.Throws<ArgumentNullException>(() => app.MapA2A(requestHandler, path!));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => app.MapA2A(requestHandler, path));
        }
    }

    [Fact]
    public void MapA2A_RequiresNonNullRequestHandler()
    {
        // Arrange
        var app = WebApplication.CreateBuilder().Build();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => app.MapA2A(null!, "/agent"));
    }

    [Fact]
    public void MapWellKnownAgentCard_RequiresNonNullAgentCard()
    {
        // Arrange
        var app = WebApplication.CreateBuilder().Build();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => app.MapWellKnownAgentCard(null!));
    }

    private static async Task<HttpResponse> ExecuteAgentCardEndpointAsync(
        AgentCard agentCard,
        AgentCardCacheOptions? cacheOptions = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Add(A2AJsonUtilities.DefaultOptions.TypeInfoResolver!));
        var app = builder.Build();
        app.MapWellKnownAgentCard(agentCard, cacheOptions: cacheOptions);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single();
        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        return context.Response;
    }

    private static async Task<(int StatusCode, CancelTaskRequest? Request, RequestContext? Context)> ExecuteCancelTaskEndpointAsync(
        string? body, bool includeContentLength = true)
    {
        var agent = new CancellationAgent();
        var store = new InMemoryTaskStore();
        await store.SaveTaskAsync("route-task", new AgentTask
        {
            Id = "route-task",
            ContextId = "context",
            Status = new() { State = TaskState.Working },
        });
        await using var handler = new CancellationRequestHandler(agent, store);
        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolver = A2AJsonUtilities.DefaultOptions.TypeInfoResolver);
        await using var app = builder.Build();
        app.MapHttpA2A(handler);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/tasks/{id}:cancel");

        using var requestBody = new MemoryStream(Encoding.UTF8.GetBytes(body ?? string.Empty));
        using var responseBody = new MemoryStream();
        using var scope = app.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = "POST";
        context.Request.Path = "/tasks/route-task:cancel";
        context.Request.RouteValues["id"] = "route-task";
        context.Request.ContentType = body is null ? null : "application/json";
        context.Request.ContentLength = includeContentLength ? requestBody.Length : null;
        context.Request.Body = requestBody;
        context.Response.Body = responseBody;
        var canHaveBody = body is not null;
        context.Features.Set(Mock.Of<IHttpRequestBodyDetectionFeature>(
            feature => feature.CanHaveBody == canHaveBody));

        await endpoint.RequestDelegate!(context);

        return (context.Response.StatusCode, handler.Request, agent.Context);
    }

    private sealed class CancellationRequestHandler(CancellationAgent agent, InMemoryTaskStore store)
        : A2AServer(agent, store, new ChannelEventNotifier(), NullLogger<A2AServer>.Instance)
    {
        public CancelTaskRequest? Request { get; private set; }

        public override Task<AgentTask> CancelTaskAsync(
            CancelTaskRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default)
        {
            Request = request;
            return base.CancelTaskAsync(request, requestContext, cancellationToken);
        }
    }

    private sealed class CancellationAgent : IAgentHandler
    {
        public RequestContext? Context { get; private set; }

        public Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public async Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            Context = context;
            await new TaskUpdater(eventQueue, context.TaskId, context.ContextId)
                .CancelAsync(cancellationToken: cancellationToken);
        }
    }

    [Fact]
    public async Task MapHttpA2A_UnsupportedVersionHeader_Returns400()
    {
        // GitHub issue #512: HTTP+JSON must reject an unsupported A2A-Version, matching
        // JSON-RPC, mapping VersionNotSupported to HTTP 400 without invoking the handler.
        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolver = A2AJsonUtilities.DefaultOptions.TypeInfoResolver);
        await using var app = builder.Build();
        var requestHandler = new Mock<IA2ARequestHandler>(MockBehavior.Strict);
        app.MapHttpA2A(requestHandler.Object);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/tasks/{id}" &&
                candidate.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));

        using var responseBody = new MemoryStream();
        using var scope = app.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = "GET";
        context.Request.Path = "/tasks/task-1";
        context.Request.RouteValues["id"] = "task-1";
        context.Request.Headers["A2A-Version"] = "99.0";
        context.Response.Body = responseBody;

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        responseBody.Position = 0;
        using var responseJson = await JsonDocument.ParseAsync(responseBody);
        Assert.Equal(
            "VERSION_NOT_SUPPORTED",
            responseJson.RootElement.GetProperty("error").GetProperty("details")[0].GetProperty("reason").GetString());
        requestHandler.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("0.3")]
    [InlineData(null)]
    public async Task MapHttpA2A_SupportedOrAbsentVersion_PassesFilter(string? version)
    {
        // GitHub issue #512: a supported or absent A2A-Version must pass the preflight and
        // reach the handler (the filter must not reject valid requests).
        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolver = A2AJsonUtilities.DefaultOptions.TypeInfoResolver);
        await using var app = builder.Build();
        var requestHandler = new Mock<IA2ARequestHandler>(MockBehavior.Strict);
        requestHandler
            .Setup(handler => handler.GetTaskAsync(It.IsAny<GetTaskRequest>(), It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTask { Id = "task-1", ContextId = "ctx-1" });
        app.MapHttpA2A(requestHandler.Object);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/tasks/{id}" &&
                candidate.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));

        using var responseBody = new MemoryStream();
        using var scope = app.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = "GET";
        context.Request.Path = "/tasks/task-1";
        context.Request.RouteValues["id"] = "task-1";
        if (version is not null)
        {
            context.Request.Headers["A2A-Version"] = version;
        }
        context.Response.Body = responseBody;

        await endpoint.RequestDelegate!(context);

        Assert.NotEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        requestHandler.Verify(
            handler => handler.GetTaskAsync(It.IsAny<GetTaskRequest>(), It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
    [Fact]
    public async Task MapHttpA2A_MultiValueVersionHeader_WithUnsupportedValue_Returns400()
    {
        // GitHub issue #512 hardening: a repeated A2A-Version header carrying a supported value
        // followed by an unsupported one must still be rejected (no first-value bypass).
        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolver = A2AJsonUtilities.DefaultOptions.TypeInfoResolver);
        await using var app = builder.Build();
        var requestHandler = new Mock<IA2ARequestHandler>(MockBehavior.Strict);
        app.MapHttpA2A(requestHandler.Object);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/tasks/{id}" &&
                candidate.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));

        using var responseBody = new MemoryStream();
        using var scope = app.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = "GET";
        context.Request.Path = "/tasks/task-1";
        context.Request.RouteValues["id"] = "task-1";
        context.Request.Headers["A2A-Version"] = new Microsoft.Extensions.Primitives.StringValues(["1.0", "99.0"]);
        context.Response.Body = responseBody;

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        requestHandler.VerifyNoOtherCalls();
    }
}

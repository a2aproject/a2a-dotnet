using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace A2A.AspNetCore.Tests;

public class A2AEndpointRouteBuilderExtensionsTests
{
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
                It.IsAny<CancellationToken>()))
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
}

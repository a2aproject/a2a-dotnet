using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;

namespace A2A.AspNetCore.Tests;

public class A2AHttpProcessorTests
{
    [Fact]
    public async Task GetAgentCard_ShouldReturnValidJsonResult()
    {
        // Arrange
        var agentCardProvider = new AgentCardProvider();
        var logger = NullLogger.Instance;

        // Act
        var result = await A2AHttpProcessor.GetAgentCardAsync(agentCardProvider, logger, "http://example.com", CancellationToken.None);
        (int statusCode, string? contentType, AgentCard agentCard) = await GetAgentCardResponse(result);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.Equal("application/json; charset=utf-8", contentType);
        Assert.Equal("Unknown", agentCard.Name);
    }

    [Fact]
    public async Task GetTask_ShouldReturnNotNull()
    {
        // Arrange
        var taskStore = new InMemoryTaskStore();
        await taskStore.SetTaskAsync(new AgentTask
        {
            Id = "testId",
        });
        var taskManager = new TaskManager(taskStore: taskStore);
        var logger = NullLogger.Instance;
        var id = "testId";
        var historyLength = 10;

        // Act
        var result = await A2AHttpProcessor.GetTaskAsync(taskManager, logger, id, historyLength, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<A2AResponseResult>(result);
    }

    [Fact]
    public async Task CancelTask_ShouldReturnNotNull()
    {
        // Arrange
        var taskStore = new InMemoryTaskStore();
        await taskStore.SetTaskAsync(new AgentTask
        {
            Id = "testId",
        });
        var taskManager = new TaskManager(taskStore: taskStore);
        var logger = NullLogger.Instance;
        var id = "testId";

        // Act
        var result = await A2AHttpProcessor.CancelTaskAsync(taskManager, logger, id, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<A2AResponseResult>(result);
    }

    [Fact]
    public async Task SendTaskMessage_ShouldReturnNotNull()
    {
        // Arrange
        var taskStore = new InMemoryTaskStore();
        await taskStore.SetTaskAsync(new AgentTask
        {
            Id = "testId",
        });
        var taskManager = new TaskManager(taskStore: taskStore);
        var logger = NullLogger.Instance;
        var sendParams = new MessageSendParams
        {
            Message = { TaskId = "testId" },
            Configuration = new() { HistoryLength = 10 }
        };

        // Act
        var result = await A2AHttpProcessor.SendMessageAsync(taskManager, logger, sendParams, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<A2AResponseResult>(result);
    }

    [Theory]
    [InlineData(A2AErrorCode.TaskNotFound, StatusCodes.Status404NotFound)]
    [InlineData(A2AErrorCode.MethodNotFound, StatusCodes.Status404NotFound)]
    [InlineData(A2AErrorCode.InvalidRequest, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.InvalidParams, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.TaskNotCancelable, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.UnsupportedOperation, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.ParseError, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.PushNotificationNotSupported, StatusCodes.Status400BadRequest)]
    [InlineData(A2AErrorCode.ContentTypeNotSupported, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(A2AErrorCode.InternalError, StatusCodes.Status500InternalServerError)]
    public async Task GetTask_WithA2AException_ShouldMapToCorrectHttpStatusCode(A2AErrorCode errorCode, int expectedStatusCode)
    {
        // Arrange
        var mockTaskStore = new Mock<ITaskStore>();
        mockTaskStore
            .Setup(ts => ts.GetTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new A2AException("Test exception", errorCode));

        var taskManager = new TaskManager(taskStore: mockTaskStore.Object);
        var logger = NullLogger.Instance;
        var id = "testId";
        var historyLength = 10;

        // Act
        var result = await A2AHttpProcessor.GetTaskAsync(taskManager, logger, id, historyLength, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedStatusCode, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task GetTask_WithUnknownA2AErrorCode_ShouldReturn500InternalServerError()
    {
        // Arrange
        var mockTaskStore = new Mock<ITaskStore>();
        // Create an A2AException with an unknown/invalid error code by casting an integer that doesn't correspond to any enum value
        var unknownErrorCode = (A2AErrorCode)(-99999);
        mockTaskStore
            .Setup(ts => ts.GetTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new A2AException("Test exception with unknown error code", unknownErrorCode));

        var taskManager = new TaskManager(taskStore: mockTaskStore.Object);
        var logger = NullLogger.Instance;
        var id = "testId";
        var historyLength = 10;

        // Act
        var result = await A2AHttpProcessor.GetTaskAsync(taskManager, logger, id, historyLength, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, ((IStatusCodeHttpResult)result).StatusCode);
    }

    private static async Task<(int statusCode, string? contentType, AgentCard agentCard)> GetAgentCardResponse(IResult responseResult)
    {
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(new NullLoggerFactory());
        services.Configure<JsonOptions>(jsonOptions => jsonOptions.SerializerOptions.TypeInfoResolver = A2AJsonUtilities.DefaultOptions.TypeInfoResolver);
        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        HttpContext context = new DefaultHttpContext()
        {
            RequestServices = serviceProvider
        };
        using MemoryStream memoryStream = new();
        context.Response.Body = memoryStream;

        await responseResult.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var card = await JsonSerializer.DeserializeAsync<AgentCard>(context.Response.Body, A2AJsonUtilities.DefaultOptions);
        return (context.Response.StatusCode, context.Response.ContentType, card!);
    }

    [Fact]
    public async Task GetAuthenticatedAgentCardCoreAsync_WithAuthenticatedUser_ReturnsExtendedCard()
    {
        // Arrange
        var agentCardProvider = new AgentCardProvider();
        var agentUrl = "https://example.com/agent";

        // Set up standard agent card handler
        agentCardProvider.OnAgentCardQuery = (url, cancellationToken) => Task.FromResult(new AgentCard
        {
            Name = "Standard Agent",
            Url = url,
            Description = "Standard description",
            SupportsAuthenticatedExtendedCard = true
        });

        // Set up authenticated agent card handler
        agentCardProvider.OnAuthenticatedAgentCardQuery = (url, authContext, cancellationToken) => Task.FromResult(new AgentCard
        {
            Name = "Extended Agent",
            Url = url,
            Description = "Extended description with additional capabilities",
            SupportsAuthenticatedExtendedCard = true,
            Skills = [
                new AgentSkill
                {
                    Id = "admin-skill",
                    Name = "Admin Skill",
                    Description = "Administrative capabilities available only to authenticated users"
                }
            ]
        });

        var authContext = new AuthenticationContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "testuser"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "admin")
            ], "Bearer")),
            Scheme = "Bearer"
        };

        // Act
        var agentCard = await A2AHttpProcessor.GetAuthenticatedAgentCardCoreAsync(agentCardProvider, agentUrl, authContext, CancellationToken.None);

        // Assert
        Assert.Equal("Extended Agent", agentCard.Name);
        Assert.Equal("Extended description with additional capabilities", agentCard.Description);
        Assert.True(agentCard.SupportsAuthenticatedExtendedCard);
        Assert.Single(agentCard.Skills);
        Assert.Equal("admin-skill", agentCard.Skills[0].Id);
    }

    [Fact]
    public async Task GetAuthenticatedAgentCardCoreAsync_WithoutAuthenticatedUser_ThrowsAuthenticationRequired()
    {
        // Arrange
        var agentCardProvider = new AgentCardProvider();
        var agentUrl = "https://example.com/agent";

        // Set up standard agent card handler
        agentCardProvider.OnAgentCardQuery = (url, cancellationToken) => Task.FromResult(new AgentCard
        {
            Name = "Standard Agent",
            Url = url,
            Description = "Standard description",
            SupportsAuthenticatedExtendedCard = true
        });

        // Set up authenticated agent card handler  
        agentCardProvider.OnAuthenticatedAgentCardQuery = (url, authContext, cancellationToken) => Task.FromResult(new AgentCard
        {
            Name = "Extended Agent",
            Url = url,
            Description = "Extended description"
        });

        // Act & Assert - no authentication context
        var exception = await Assert.ThrowsAsync<A2AException>(() =>
            A2AHttpProcessor.GetAuthenticatedAgentCardCoreAsync(agentCardProvider, agentUrl, null, CancellationToken.None));

        Assert.Equal(A2AErrorCode.AuthenticationRequired, exception.ErrorCode);
        Assert.Equal("Authentication required to access extended agent card", exception.Message);
    }

    [Fact]
    public async Task GetAuthenticatedAgentCardCoreAsync_WithoutAuthenticatedHandler_ThrowsAuthenticationRequired()
    {
        // Arrange
        var agentCardProvider = new AgentCardProvider();
        var agentUrl = "https://example.com/agent";

        // Set up only standard agent card handler (no authenticated handler)
        agentCardProvider.OnAgentCardQuery = (url, cancellationToken) => Task.FromResult(new AgentCard
        {
            Name = "Standard Agent",
            Url = url,
            Description = "Standard description"
        });

        var authContext = new AuthenticationContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "testuser")
            ], "Bearer")),
            Scheme = "Bearer"
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<A2AException>(() =>
            A2AHttpProcessor.GetAuthenticatedAgentCardCoreAsync(agentCardProvider, agentUrl, authContext, CancellationToken.None));

        Assert.Equal(A2AErrorCode.AuthenticationRequired, exception.ErrorCode);
        Assert.Equal("Extended authenticated agent card is not supported", exception.Message);
    }

    [Fact]
    public void ExtractAuthenticationContext_WithAuthenticatedUser_ReturnsAuthContext()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "testuser"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "admin")
        ], "Bearer"));

        // Act
        var result = A2AHttpProcessor.ExtractAuthenticationContext(httpContext.Request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsAuthenticated);
        Assert.Equal("testuser", result.UserName);
        Assert.Equal("Bearer", result.Scheme);
        Assert.True(result.HasClaim(System.Security.Claims.ClaimTypes.Name, "testuser"));
        Assert.True(result.HasClaim(System.Security.Claims.ClaimTypes.Role, "admin"));
    }

    [Fact]
    public void ExtractAuthenticationContext_WithoutAuthenticatedUser_ReturnsNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        // No authenticated user

        // Act
        var result = A2AHttpProcessor.ExtractAuthenticationContext(httpContext.Request);

        // Assert
        Assert.Null(result);
    }
}
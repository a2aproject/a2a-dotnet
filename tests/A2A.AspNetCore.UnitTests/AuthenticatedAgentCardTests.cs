using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Xunit;

namespace A2A.AspNetCore.UnitTests;

public class AuthenticatedAgentCardTests
{
    [Fact]
    public async Task GetAuthenticatedAgentCardAsync_WithAuthenticatedUser_ReturnsExtendedCard()
    {
        // Arrange
        var taskManager = new TaskManager();
        var logger = CreateLogger();
        var agentUrl = "https://example.com/agent";

        // Set up standard agent card handler
        taskManager.OnAgentCardQuery = (url, ct) => Task.FromResult(new AgentCard
        {
            Name = "Standard Agent",
            Url = url,
            Description = "Standard description",
            SupportsAuthenticatedExtendedCard = true
        });

        // Set up authenticated agent card handler
        taskManager.OnAuthenticatedAgentCardQuery = (url, authContext, ct) => Task.FromResult(new AgentCard
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
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.Name, "testuser"),
                new Claim(ClaimTypes.Role, "admin")
            ], "Bearer")),
            Scheme = "Bearer"
        };

        // Act
        var result = await A2AHttpProcessor.GetAuthenticatedAgentCardAsync(taskManager, logger, agentUrl, authContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<AgentCard>>(result);
        var agentCard = okResult.Value;

        Assert.Equal("Extended Agent", agentCard.Name);
        Assert.Equal("Extended description with additional capabilities", agentCard.Description);
        Assert.True(agentCard.SupportsAuthenticatedExtendedCard);
        Assert.Single(agentCard.Skills);
        Assert.Equal("admin-skill", agentCard.Skills[0].Id);
    }

    [Fact]
    public async Task GetAuthenticatedAgentCardAsync_WithoutAuthenticatedUser_ReturnsStandardCard()
    {
        // Arrange
        var taskManager = new TaskManager();
        var logger = CreateLogger();
        var agentUrl = "https://example.com/agent";

        // Set up standard agent card handler
        taskManager.OnAgentCardQuery = (url, ct) => Task.FromResult(new AgentCard
        {
            Name = "Standard Agent",
            Url = url,
            Description = "Standard description",
            SupportsAuthenticatedExtendedCard = true
        });

        // Set up authenticated agent card handler  
        taskManager.OnAuthenticatedAgentCardQuery = (url, authContext, ct) => Task.FromResult(new AgentCard
        {
            Name = "Extended Agent",
            Url = url,
            Description = "Extended description"
        });

        // Act - no authentication context
        var result = await A2AHttpProcessor.GetAuthenticatedAgentCardAsync(taskManager, logger, agentUrl, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<AgentCard>>(result);
        var agentCard = okResult.Value;

        Assert.Equal("Standard Agent", agentCard.Name);
        Assert.Equal("Standard description", agentCard.Description);
        Assert.Empty(agentCard.Skills); // Standard card has no admin skills
    }

    [Fact]
    public async Task GetAuthenticatedAgentCardAsync_WithoutAuthenticatedHandler_ReturnsStandardCard()
    {
        // Arrange
        var taskManager = new TaskManager();
        var logger = CreateLogger();
        var agentUrl = "https://example.com/agent";

        // Set up only standard agent card handler (no authenticated handler)
        taskManager.OnAgentCardQuery = (url, ct) => Task.FromResult(new AgentCard
        {
            Name = "Standard Agent",
            Url = url,
            Description = "Standard description"
        });

        var authContext = new AuthenticationContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.Name, "testuser")
            ], "Bearer")),
            Scheme = "Bearer"
        };

        // Act
        var result = await A2AHttpProcessor.GetAuthenticatedAgentCardAsync(taskManager, logger, agentUrl, authContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<AgentCard>>(result);
        var agentCard = okResult.Value;

        Assert.Equal("Standard Agent", agentCard.Name);
        Assert.Equal("Standard description", agentCard.Description);
    }

    [Fact]
    public void ExtractAuthenticationContext_WithAuthenticatedUser_ReturnsAuthContext()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(ClaimTypes.Role, "admin")
        ], "Bearer"));

        // Act
        var result = A2AHttpProcessor.ExtractAuthenticationContext(httpContext.Request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsAuthenticated);
        Assert.Equal("testuser", result.UserName);
        Assert.Equal("Bearer", result.Scheme);
        Assert.True(result.HasClaim(ClaimTypes.Name, "testuser"));
        Assert.True(result.HasClaim(ClaimTypes.Role, "admin"));
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

    private static Microsoft.Extensions.Logging.Abstractions.NullLogger CreateLogger()
    {
        return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }
}
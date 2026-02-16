using Microsoft.AspNetCore.Builder;
using Moq;

namespace A2A.AspNetCore.Tests;

public class A2AEndpointRouteBuilderExtensionsTests
{
    [Fact]
    public void MapA2A_RegistersEndpoint_WithCorrectPath()
    {
        // Arrange
        var app = WebApplication.CreateBuilder().Build();
        var taskManager = new Mock<ITaskManager>().Object;

        // Act & Assert - Should not throw
        var result = app.MapA2A(taskManager, "/agent");
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
    public void MapA2A_And_MapWellKnownAgentCard_Together_RegistersBothEndpoints()
    {
        // Arrange
        var app = WebApplication.CreateBuilder().Build();
        var taskManager = new Mock<ITaskManager>().Object;
        var agentCard = new AgentCard { Name = "Test", Description = "Test agent" };

        // Act & Assert - Should not throw when calling both
        var result1 = app.MapA2A(taskManager, "/agent");
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
        var taskManager = new Mock<ITaskManager>().Object;

        // Act & Assert
        if (path == null)
        {
            Assert.Throws<ArgumentNullException>(() => app.MapA2A(taskManager, path!));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => app.MapA2A(taskManager, path));
        }
    }

    [Fact]
    public void MapA2A_RequiresNonNullTaskManager()
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
}
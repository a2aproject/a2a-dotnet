using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace A2A.AspNetCore.Tests;

public class A2AEndpointRouteBuilderExtensionsTests
{
    [Fact]
    public void MapA2A_RegistersEndpoint_WithCorrectPath()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddRouting();
        var services = serviceCollection.BuildServiceProvider();
        
        var app = WebApplication.CreateBuilder().Build();
        var taskManager = new TaskManager();

        // Act & Assert - Should not throw
        var result = app.MapA2A(taskManager, "/agent");
        Assert.NotNull(result);
    }

    [Fact]
    public void MapWellKnownAgentCard_RegistersEndpoint_WithCorrectPath()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddRouting();
        var services = serviceCollection.BuildServiceProvider();
        
        var app = WebApplication.CreateBuilder().Build();
        var taskManager = new TaskManager();

        // Act & Assert - Should not throw
        var result = app.MapWellKnownAgentCard(taskManager, "/agent");
        Assert.NotNull(result);
    }

    [Fact]
    public void MapA2A_And_MapWellKnownAgentCard_Together_RegistersBothEndpoints()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddRouting();
        var services = serviceCollection.BuildServiceProvider();
        
        var app = WebApplication.CreateBuilder().Build();
        var taskManager = new TaskManager();

        // Act & Assert - Should not throw when calling both
        var result1 = app.MapA2A(taskManager, "/agent");
        var result2 = app.MapWellKnownAgentCard(taskManager, "/agent");
        
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
        var taskManager = new TaskManager();

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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MapWellKnownAgentCard_ThrowsArgumentException_WhenAgentPathIsNullOrEmpty(string? agentPath)
    {
        // Arrange
        var app = WebApplication.CreateBuilder().Build();
        var taskManager = new TaskManager();

        // Act & Assert
        if (agentPath == null)
        {
            Assert.Throws<ArgumentNullException>(() => app.MapWellKnownAgentCard(taskManager, agentPath!));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => app.MapWellKnownAgentCard(taskManager, agentPath));
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
    public void MapWellKnownAgentCard_RequiresNonNullTaskManager()
    {
        // Arrange
        var app = WebApplication.CreateBuilder().Build();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => app.MapWellKnownAgentCard(null!, "/agent"));
    }
}
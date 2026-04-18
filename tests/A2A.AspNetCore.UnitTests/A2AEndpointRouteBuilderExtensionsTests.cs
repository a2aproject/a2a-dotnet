using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Moq;

namespace A2A.AspNetCore.Tests;

public class A2AEndpointRouteBuilderExtensionsTests
{
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
        Assert.Throws<ArgumentNullException>(() => app.MapA2A((IA2ARequestHandler)null!, "/agent"));
    }

    [Fact]
    public void MapWellKnownAgentCard_RequiresNonNullAgentCard()
    {
        // Arrange
        var app = WebApplication.CreateBuilder().Build();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => app.MapWellKnownAgentCard((AgentCard)null!));
    }

    // ── Factory overload tests ──

    [Fact]
    public void MapA2A_WithFactory_RegistersEndpoint()
    {
        var app = WebApplication.CreateBuilder().Build();
        Func<HttpContext, IA2ARequestHandler> factory = _ => new Mock<IA2ARequestHandler>().Object;
        var result = app.MapA2A(factory, "/agent");
        Assert.NotNull(result);
    }

    [Fact]
    public void MapA2A_WithFactory_ThrowsArgumentNullException_WhenFactoryIsNull()
    {
        var app = WebApplication.CreateBuilder().Build();
        Assert.Throws<ArgumentNullException>(() =>
            app.MapA2A((Func<HttpContext, IA2ARequestHandler>)null!, "/agent"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MapA2A_WithFactory_ThrowsArgumentException_WhenPathIsNullOrEmpty(string? path)
    {
        var app = WebApplication.CreateBuilder().Build();
        Func<HttpContext, IA2ARequestHandler> factory = _ => new Mock<IA2ARequestHandler>().Object;
        if (path == null)
            Assert.Throws<ArgumentNullException>(() => app.MapA2A(factory, path!));
        else
            Assert.Throws<ArgumentException>(() => app.MapA2A(factory, path));
    }

    [Fact]
    public void MapA2A_WithHandlerAndCardFactories_RegistersEndpoint()
    {
        var app = WebApplication.CreateBuilder().Build();
        Func<HttpContext, IA2ARequestHandler> handlerFactory = _ => new Mock<IA2ARequestHandler>().Object;
        Func<HttpContext, AgentCard> cardFactory = _ => new AgentCard { Name = "Test", Description = "Test" };
        var result = app.MapA2A(handlerFactory, cardFactory, "/agent");
        Assert.NotNull(result);
    }

    [Fact]
    public void MapWellKnownAgentCard_WithFactory_RegistersEndpoint()
    {
        var app = WebApplication.CreateBuilder().Build();
        Func<HttpContext, AgentCard> factory = _ => new AgentCard { Name = "Test", Description = "Test" };
        var result = app.MapWellKnownAgentCard(factory);
        Assert.NotNull(result);
    }

    [Fact]
    public void MapWellKnownAgentCard_WithFactory_ThrowsArgumentNullException_WhenFactoryIsNull()
    {
        var app = WebApplication.CreateBuilder().Build();
        Assert.Throws<ArgumentNullException>(() =>
            app.MapWellKnownAgentCard((Func<HttpContext, AgentCard>)null!));
    }

    [Fact]
    public void MapHttpA2A_WithFactories_RegistersEndpoint()
    {
        var app = WebApplication.CreateBuilder().Build();
        Func<HttpContext, IA2ARequestHandler> handlerFactory = _ => new Mock<IA2ARequestHandler>().Object;
        Func<HttpContext, AgentCard> cardFactory = _ => new AgentCard { Name = "Test", Description = "Test" };
        var result = app.MapHttpA2A(handlerFactory, cardFactory);
        Assert.NotNull(result);
    }

    [Fact]
    public void MapHttpA2A_WithFactories_ThrowsArgumentNullException_WhenHandlerFactoryIsNull()
    {
        var app = WebApplication.CreateBuilder().Build();
        Func<HttpContext, AgentCard> cardFactory = _ => new AgentCard { Name = "Test", Description = "Test" };
        Assert.Throws<ArgumentNullException>(() =>
            app.MapHttpA2A((Func<HttpContext, IA2ARequestHandler>)null!, cardFactory));
    }

    [Fact]
    public void MapHttpA2A_WithFactories_ThrowsArgumentNullException_WhenCardFactoryIsNull()
    {
        var app = WebApplication.CreateBuilder().Build();
        Func<HttpContext, IA2ARequestHandler> handlerFactory = _ => new Mock<IA2ARequestHandler>().Object;
        Assert.Throws<ArgumentNullException>(() =>
            app.MapHttpA2A(handlerFactory, (Func<HttpContext, AgentCard>)null!));
    }
}

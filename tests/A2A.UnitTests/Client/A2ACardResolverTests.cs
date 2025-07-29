using System.Net;
using System.Text;
using System.Text.Json;

namespace A2A.UnitTests.Client;

public class A2ACardResolverTests
{
    [Fact]
    public async Task GetAgentCardAsync_ReturnsAgentCard()
    {
        // Arrange
        var agentCard = new AgentCard
        {
            Name = "Test Agent",
            Description = "A test agent",
            Url = "http://localhost",
            Version = "1.0.0",
            Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = false, StateTransitionHistory = true },
            Skills = [new AgentSkill { Id = "test", Name = "Test Skill", Description = "desc", Tags = [] }]
        };
        var json = JsonSerializer.Serialize(agentCard);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var handler = new MockHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler);
        var resolver = new A2ACardResolver(new Uri("http://localhost"), httpClient);

        // Act
        var result = await resolver.GetAgentCardAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(agentCard.Name, result.Name);
        Assert.Equal(agentCard.Description, result.Description);
        Assert.Equal(agentCard.Url, result.Url);
        Assert.Equal(agentCard.Version, result.Version);
        Assert.Equal(agentCard.Capabilities.Streaming, result.Capabilities.Streaming);
        Assert.Single(result.Skills);
    }

    [Fact]
    public async Task GetAgentCardAsync_ThrowsA2AExceptionOnInvalidJson()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "application/json")
        };
        var handler = new MockHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler);
        var resolver = new A2ACardResolver(new Uri("http://localhost"), httpClient);

        // Act & Assert
        await Assert.ThrowsAsync<A2AException>(() => resolver.GetAgentCardAsync());
    }

    [Fact]
    public async Task GetAgentCardAsync_ThrowsA2AExceptionOnHttpError()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var handler = new MockHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler);
        var resolver = new A2ACardResolver(new Uri("http://localhost"), httpClient);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<A2AException>(() => resolver.GetAgentCardAsync());
        Assert.NotNull(exception.InnerException);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }
}

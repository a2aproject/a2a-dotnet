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
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var resolver = new A2ACardResolver(httpClient);

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
    public async Task GetAgentCardAsync_ThrowsOnInvalidJson()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "application/json")
        };
        var handler = new MockHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var resolver = new A2ACardResolver(httpClient);

        // Act & Assert
        await Assert.ThrowsAsync<A2AClientJsonError>(() => resolver.GetAgentCardAsync());
    }

    [Fact]
    public async Task GetAgentCardAsync_ThrowsOnHttpError()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var handler = new MockHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var resolver = new A2ACardResolver(httpClient);

        // Act & Assert
        await Assert.ThrowsAsync<A2AClientHTTPError>(() => resolver.GetAgentCardAsync());
    }
}

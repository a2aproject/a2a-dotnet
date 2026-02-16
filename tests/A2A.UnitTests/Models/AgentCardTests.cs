using System.Text.Json;

namespace A2A.UnitTests.Models;

public class AgentCardTests
{
    [Fact]
    public void WhenValidAgentCard_Serialized_RoundTripsCorrectly()
    {
        var card = new AgentCard
        {
            Name = "Test Agent",
            Description = "A test agent",
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    Url = "http://localhost:5000/a2a",
                    ProtocolBinding = "JSONRPC",
                    ProtocolVersion = "1.0",
                }
            ],
        };

        var json = JsonSerializer.Serialize(card, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<AgentCard>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("Test Agent", deserialized.Name);
        Assert.Equal("A test agent", deserialized.Description);
        Assert.Single(deserialized.SupportedInterfaces);
        Assert.Equal("http://localhost:5000/a2a", deserialized.SupportedInterfaces[0].Url);
    }

    [Fact]
    public void WhenNullOptionalFields_Serialized_OmitsNullFields()
    {
        var card = new AgentCard
        {
            Name = "Agent",
            Description = "Desc",
            SupportedInterfaces = [new AgentInterface { Url = "http://test", ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" }],
        };

        var json = JsonSerializer.Serialize(card, A2AJsonUtilities.DefaultOptions);
        var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("capabilities", out _));
        Assert.True(doc.RootElement.TryGetProperty("skills", out _));
        Assert.False(doc.RootElement.TryGetProperty("securitySchemes", out _));
    }

    [Fact]
    public void AgentCard_Serialize_WithAllProperties_RoundTripsCorrectly()
    {
        // Arrange
        var agentCard = new AgentCard
        {
            Name = "Test Agent",
            Description = "A test agent for v1 serialization",
            Provider = new AgentProvider
            {
                Organization = "Test Org",
                Url = "https://testorg.com"
            },
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    Url = "https://example.com/agent",
                    ProtocolBinding = "JSONRPC",
                    ProtocolVersion = "1.0",
                }
            ],
            Capabilities = new AgentCapabilities
            {
                Streaming = true,
                PushNotifications = false,
            },
            SecuritySchemes = new Dictionary<string, SecurityScheme>
            {
                ["apiKey"] = new SecurityScheme
                {
                    ApiKeySecurityScheme = new ApiKeySecurityScheme { Name = "X-API-Key", Location = "header" }
                }
            },
            DefaultInputModes = ["text", "image"],
            DefaultOutputModes = ["text", "json"],
            Skills =
            [
                new AgentSkill
                {
                    Id = "test-skill",
                    Name = "Test Skill",
                    Description = "A test skill",
                    Tags = ["test", "skill"],
                    Examples = ["Example usage"],
                    InputModes = ["text"],
                    OutputModes = ["text"],
                }
            ],
            Signatures =
            [
                new AgentCardSignature
                {
                    Protected = "eyJhbGciOiJFUzI1NiJ9",
                    Signature = "dGVzdC1zaWduYXR1cmU"
                }
            ]
        };

        // Act
        var json = JsonSerializer.Serialize(agentCard, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<AgentCard>(json, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(agentCard.Name, deserialized.Name);
        Assert.Equal(agentCard.Description, deserialized.Description);
        Assert.NotNull(deserialized.Provider);
        Assert.Equal("Test Org", deserialized.Provider.Organization);
        Assert.Single(deserialized.SupportedInterfaces);
        Assert.Equal("https://example.com/agent", deserialized.SupportedInterfaces[0].Url);
        Assert.NotNull(deserialized.Capabilities);
        Assert.True(deserialized.Capabilities.Streaming);
        Assert.NotNull(deserialized.SecuritySchemes);
        Assert.Single(deserialized.SecuritySchemes);
        Assert.NotNull(deserialized.SecuritySchemes["apiKey"].ApiKeySecurityScheme);
        Assert.Equal("X-API-Key", deserialized.SecuritySchemes["apiKey"].ApiKeySecurityScheme!.Name);
        Assert.Equal(new List<string> { "text", "image" }, deserialized.DefaultInputModes);
        Assert.Equal(new List<string> { "text", "json" }, deserialized.DefaultOutputModes);
        Assert.NotNull(deserialized.Skills);
        Assert.Single(deserialized.Skills);
        Assert.Equal("test-skill", deserialized.Skills[0].Id);
        Assert.NotNull(deserialized.Signatures);
        Assert.Single(deserialized.Signatures);
        Assert.Equal("eyJhbGciOiJFUzI1NiJ9", deserialized.Signatures[0].Protected);
    }
}

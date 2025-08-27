using System.Text.Json;

namespace A2A.UnitTests.Models;

public class AgentSkillTests
{
    [Fact]
    public void AgentSkill_SecurityProperty_SerializesCorrectly()
    {
        // Arrange
        var skill = new AgentSkill
        {
            Id = "test-skill",
            Name = "Test Skill",
            Description = "A test skill with security",
            Tags = ["test", "security"],
            Security =
            [
                new Dictionary<string, string[]>
                {
                    { "oauth", ["read", "write"] }
                },
                new Dictionary<string, string[]>
                {
                    { "api-key", [] },
                    { "mtls", [] }
                }
            ]
        };

        // Act
        var json = JsonSerializer.Serialize(skill, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.Contains("\"security\"", json);
        Assert.Contains("\"oauth\"", json);
        Assert.Contains("\"api-key\"", json);
        Assert.Contains("\"mtls\"", json);
        Assert.Contains("\"read\"", json);
        Assert.Contains("\"write\"", json);
    }

    [Fact]
    public void AgentSkill_SecurityProperty_DeserializesCorrectly()
    {
        // Arrange
        var json = """
        {
          "id": "secure-skill",
          "name": "Secure Skill",
          "description": "A skill with security requirements",
          "tags": ["secure"],
          "security": [
            {
              "google": ["oidc"]
            },
            {
              "api-key": [],
              "mtls": []
            }
          ]
        }
        """;

        // Act
        var skill = JsonSerializer.Deserialize<AgentSkill>(json, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(skill);
        Assert.Equal("secure-skill", skill.Id);
        Assert.Equal("Secure Skill", skill.Name);
        Assert.Equal("A skill with security requirements", skill.Description);

        Assert.NotNull(skill.Security);
        Assert.Equal(2, skill.Security.Count);

        // First security requirement (google oidc)
        var firstRequirement = skill.Security[0];
        Assert.Single(firstRequirement);
        Assert.Contains("google", firstRequirement.Keys);
        Assert.Equal(["oidc"], firstRequirement["google"]);

        // Second security requirement (api-key AND mtls)
        var secondRequirement = skill.Security[1];
        Assert.Equal(2, secondRequirement.Count);
        Assert.Contains("api-key", secondRequirement.Keys);
        Assert.Contains("mtls", secondRequirement.Keys);
        Assert.Empty(secondRequirement["api-key"]);
        Assert.Empty(secondRequirement["mtls"]);
    }

    [Fact]
    public void AgentSkill_SecurityProperty_CanBeNull()
    {
        // Arrange
        var skill = new AgentSkill
        {
            Id = "simple-skill",
            Name = "Simple Skill",
            Description = "A skill without security requirements",
            Tags = ["simple"],
            Security = null
        };

        // Act
        var json = JsonSerializer.Serialize(skill, A2AJsonUtilities.DefaultOptions);
        var deserializedSkill = JsonSerializer.Deserialize<AgentSkill>(json, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.DoesNotContain("\"security\"", json); // Should be omitted when null
        Assert.NotNull(deserializedSkill);
        Assert.Null(deserializedSkill.Security);
    }

    [Fact]
    public void AgentSkill_WithAllProperties_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var originalSkill = new AgentSkill
        {
            Id = "full-skill",
            Name = "Full Skill",
            Description = "A skill with all properties",
            Tags = ["complete", "test"],
            Examples = ["Example usage 1", "Example usage 2"],
            InputModes = ["text", "image"],
            OutputModes = ["text", "json"],
            Security =
            [
                new Dictionary<string, string[]>
                {
                    { "oauth", ["read"] }
                }
            ]
        };

        // Act
        var json = JsonSerializer.Serialize(originalSkill, A2AJsonUtilities.DefaultOptions);
        var deserializedSkill = JsonSerializer.Deserialize<AgentSkill>(json, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserializedSkill);
        Assert.Equal(originalSkill.Id, deserializedSkill.Id);
        Assert.Equal(originalSkill.Name, deserializedSkill.Name);
        Assert.Equal(originalSkill.Description, deserializedSkill.Description);
        Assert.Equal(originalSkill.Tags, deserializedSkill.Tags);
        Assert.Equal(originalSkill.Examples, deserializedSkill.Examples);
        Assert.Equal(originalSkill.InputModes, deserializedSkill.InputModes);
        Assert.Equal(originalSkill.OutputModes, deserializedSkill.OutputModes);

        Assert.NotNull(deserializedSkill.Security);
        Assert.Single(deserializedSkill.Security);
        Assert.Contains("oauth", deserializedSkill.Security[0].Keys);
        Assert.Equal(["read"], deserializedSkill.Security[0]["oauth"]);
    }
}
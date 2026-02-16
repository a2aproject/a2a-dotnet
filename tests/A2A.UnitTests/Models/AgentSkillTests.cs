using System.Text.Json;

namespace A2A.UnitTests.Models;

public class AgentSkillTests
{
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
    }

    [Fact]
    public void AgentSkill_NullOptionalFields_OmittedInJson()
    {
        // Arrange
        var skill = new AgentSkill
        {
            Id = "simple-skill",
            Name = "Simple Skill",
            Description = "A skill without optional properties",
        };

        // Act
        var json = JsonSerializer.Serialize(skill, A2AJsonUtilities.DefaultOptions);
        var deserializedSkill = JsonSerializer.Deserialize<AgentSkill>(json, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.Contains("\"tags\"", json);
        Assert.DoesNotContain("\"examples\"", json);
        Assert.DoesNotContain("\"securityRequirements\"", json);
        Assert.NotNull(deserializedSkill);
        Assert.NotNull(deserializedSkill.Tags);
        Assert.Empty(deserializedSkill.Tags);
        Assert.Null(deserializedSkill.SecurityRequirements);
    }

    [Fact]
    public void AgentSkill_DeserializesFromJson()
    {
        // Arrange
        var json = """
        {
          "id": "test-skill",
          "name": "Test Skill",
          "description": "A test skill",
          "tags": ["test", "skill"],
          "examples": ["Example usage"],
          "inputModes": ["text"],
          "outputModes": ["text"]
        }
        """;

        // Act
        var skill = JsonSerializer.Deserialize<AgentSkill>(json, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(skill);
        Assert.Equal("test-skill", skill.Id);
        Assert.Equal("Test Skill", skill.Name);
        Assert.Equal("A test skill", skill.Description);
        Assert.NotNull(skill.Tags);
        Assert.Equal(2, skill.Tags.Count);
        Assert.Contains("test", skill.Tags);
        Assert.Contains("skill", skill.Tags);
    }
}
namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a skill offered by an agent.</summary>
public sealed class AgentSkill
{
    /// <summary>Gets or sets the skill identifier.</summary>
    [JsonPropertyName("id"), JsonRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the skill name.</summary>
    [JsonPropertyName("name"), JsonRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the skill description.</summary>
    [JsonPropertyName("description"), JsonRequired]
    public string Description { get; set; } = string.Empty;

    /// <summary>Tags categorizing the skill.</summary>
    [JsonPropertyName("tags"), JsonRequired]
    public List<string> Tags { get; set; } = [];

    /// <summary>Gets or sets the examples for this skill.</summary>
    [JsonPropertyName("examples")]
    public List<string>? Examples { get; set; }

    /// <summary>Gets or sets the input modes supported by this skill.</summary>
    [JsonPropertyName("inputModes")]
    public List<string>? InputModes { get; set; }

    /// <summary>Gets or sets the output modes supported by this skill.</summary>
    [JsonPropertyName("outputModes")]
    public List<string>? OutputModes { get; set; }

    /// <summary>Gets or sets the security requirements for this skill.</summary>
    [JsonPropertyName("securityRequirements")]
    public List<SecurityRequirement>? SecurityRequirements { get; set; }
}

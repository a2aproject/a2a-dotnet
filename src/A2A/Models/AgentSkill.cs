namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a skill offered by an agent.</summary>
public sealed class AgentSkill
{
    /// <summary>Gets or sets the skill identifier.</summary>
    [JsonRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the skill name.</summary>
    [JsonRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the skill description.</summary>
    [JsonRequired]
    public string Description { get; set; } = string.Empty;

    /// <summary>Tags categorizing the skill.</summary>
    [JsonRequired]
    public List<string> Tags { get; set; } = [];

    /// <summary>Gets or sets the examples for this skill.</summary>
    public List<string>? Examples { get; set; }

    /// <summary>Gets or sets the input modes supported by this skill.</summary>
    public List<string>? InputModes { get; set; }

    /// <summary>Gets or sets the output modes supported by this skill.</summary>
    public List<string>? OutputModes { get; set; }

    /// <summary>Gets or sets the security requirements for this skill.</summary>
    public List<SecurityRequirement>? SecurityRequirements { get; set; }
}

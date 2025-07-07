using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a unit of capability that an agent can perform.
/// </summary>
public class AgentSkill
{
    /// <summary>
    /// Unique identifier for the agent's skill.
    /// </summary>
    [JsonPropertyName("id")]
    [Required]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human readable name of the skill.
    /// </summary>
    [JsonPropertyName("name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of the skill - will be used by the client or a human
    /// as a hint to understand what the skill does.
    /// </summary>
    [JsonPropertyName("description")]
    [Required]
    public string? Description { get; set; }

    /// <summary>
    /// Set of tagwords describing classes of capabilities for this specific skill.
    /// </summary>
    [JsonPropertyName("tags")]
    [Required]
    public List<string>? Tags { get; set; }

    /// <summary>
    /// The set of example scenarios that the skill can perform.
    /// Will be used by the client as a hint to understand how the skill can be used.
    /// </summary>
    [JsonPropertyName("examples")]
    public List<string>? Examples { get; set; }

    /// <summary>
    /// The set of interaction modes that the skill supports
    /// (if different than the default).
    /// Supported media types for input.
    /// </summary>
    [JsonPropertyName("inputModes")]
    public List<string>? InputModes { get; set; }

    /// <summary>
    /// Supported media types for output.
    /// </summary>
    [JsonPropertyName("outputModes")]
    public List<string>? OutputModes { get; set; }
}

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace A2A.Core;

/// <summary>
/// Information about a skill provided by an agent
/// </summary>
public class AgentSkill
{
    /// <summary>
    /// The skill identifier
    /// </summary>
    [JsonPropertyName("id")]
    [Required]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The skill name
    /// </summary>
    [JsonPropertyName("name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the skill
    /// </summary>
    [JsonPropertyName("description")]
    [Required]
    public string? Description { get; set; }

    /// <summary>
    /// Optional tags for the skill
    /// </summary>
    [JsonPropertyName("tags")]
    [Required]
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Optional examples of using the skill
    /// </summary>
    [JsonPropertyName("examples")]
    public List<string>? Examples { get; set; }

    /// <summary>
    /// Optional input modes supported by the skill
    /// </summary>
    [JsonPropertyName("inputModes")]
    public List<string>? InputModes { get; set; }

    /// <summary>
    /// Optional output modes supported by the skill
    /// </summary>
    [JsonPropertyName("outputModes")]
    public List<string>? OutputModes { get; set; }
}

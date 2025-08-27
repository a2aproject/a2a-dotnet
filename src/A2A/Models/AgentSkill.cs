using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a unit of capability that an agent can perform.
/// </summary>
public sealed class AgentSkill
{
    /// <summary>
    /// Unique identifier for the agent's skill.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human readable name of the skill.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of the skill.
    /// </summary>
    /// <remarks>
    /// Will be used by the client or a human as a hint to understand what the skill does.
    /// </remarks>
    [JsonPropertyName("description")]
    [JsonRequired]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Set of tagwords describing classes of capabilities for this specific skill.
    /// </summary>
    [JsonPropertyName("tags")]
    [JsonRequired]
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// The set of example scenarios that the skill can perform.
    /// </summary>
    /// <remarks>
    /// Will be used by the client as a hint to understand how the skill can be used.
    /// </remarks>
    [JsonPropertyName("examples")]
    public List<string>? Examples { get; set; }

    /// <summary>
    /// The set of interaction modes that the skill supports (if different than the default).
    /// </summary>
    /// <remarks>
    /// Supported media types for input.
    /// </remarks>
    [JsonPropertyName("inputModes")]
    public List<string>? InputModes { get; set; }

    /// <summary>
    /// Supported media types for output.
    /// </summary>
    [JsonPropertyName("outputModes")]
    public List<string>? OutputModes { get; set; }

    /// <summary>
    /// Security schemes necessary for the agent to leverage this skill.
    /// </summary>
    /// <remarks>
    /// As in the overall AgentCard.security, this list represents a logical OR of security
    /// requirement objects. Each object is a set of security schemes that must be used together
    /// (a logical AND).
    /// </remarks>
    [JsonPropertyName("security")]
    public List<Dictionary<string, string[]>>? Security { get; set; }
}

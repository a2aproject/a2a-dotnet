using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.V0_3;

/// <summary>
/// Represents an artifact generated for a task.
/// </summary>
public sealed class Artifact
{
    /// <summary>
    /// Unique identifier for the artifact.
    /// </summary>
    [JsonPropertyName("artifactId")]
    [JsonRequired]
    public string ArtifactId { get; set; } = string.Empty;

    /// <summary>
    /// Optional name for the artifact.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Optional description for the artifact.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Artifact parts.
    /// </summary>
    [JsonPropertyName("parts")]
    [JsonRequired]
    public List<Part> Parts { get; set; } = [];

    /// <summary>
    /// Extension metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    /// <summary>
    /// The URIs of extensions that are present or contributed to this Artifact.
    /// </summary>
    [JsonPropertyName("extensions")]
    public List<string>? Extensions { get; set; }
}
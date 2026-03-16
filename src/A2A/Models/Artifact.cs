namespace A2A;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Represents an artifact produced by a task.</summary>
public sealed class Artifact
{
    /// <summary>Gets or sets the artifact identifier.</summary>
    [JsonPropertyName("artifactId"), JsonRequired]
    public string ArtifactId { get; set; } = string.Empty;

    /// <summary>Gets or sets the name of the artifact.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the description of the artifact.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Gets or sets the parts comprising this artifact.</summary>
    [JsonPropertyName("parts"), JsonRequired]
    public List<Part> Parts { get; set; } = [];

    /// <summary>Gets or sets the extensions applied to this artifact.</summary>
    [JsonPropertyName("extensions")]
    public List<string>? Extensions { get; set; }

    /// <summary>Gets or sets the metadata associated with this artifact.</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}
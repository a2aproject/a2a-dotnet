using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

public class Artifact
{

    [JsonPropertyName("artifactId")]
    [JsonRequired]
    public string ArtifactId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("parts")]
    [JsonRequired]
    public List<Part> Parts { get; set; } = new List<Part>();

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }

}



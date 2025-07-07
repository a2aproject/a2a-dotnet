using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a structured data segment within a message part.
/// </summary>
public class DataPart : Part
{
    /// <summary>
    /// Structured data content.
    /// </summary>
    [JsonPropertyName("data")]
    public Dictionary<string, JsonElement> Data { get; set; } = [];
}
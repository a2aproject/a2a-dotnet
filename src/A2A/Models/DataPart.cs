using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a structured data segment within a message part.
/// </summary>
public sealed class DataPart : Part
{
    /// <summary>
    /// Structured data content.
    /// </summary>
    [JsonPropertyName("data")]
    [JsonRequired]
    public Dictionary<string, JsonElement> Data { get; set; } = [];
}
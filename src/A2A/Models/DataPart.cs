using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a data-based part of a message or content containing structured data.
/// </summary>
public class DataPart : Part
{
    /// <summary>
    /// Gets or sets the structured data as a dictionary of key-value pairs.
    /// </summary>
    [JsonPropertyName("data")]
    public Dictionary<string, JsonElement> Data { get; set; } = [];
}
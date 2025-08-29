using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a structured data segment within a message part.
/// </summary>
public sealed class DataPart : Part
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataPart"/> class.
    /// </summary>
    public DataPart() : base(PartKind.Data)
    {
    }
    /// <summary>
    /// Structured data content.
    /// </summary>
    [JsonPropertyName("data")]
    [JsonRequired]
    public Dictionary<string, JsonElement> Data { get; set; } = [];
}
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a text-based part of a message or content.
/// </summary>
public class TextPart : Part
{
    /// <summary>
    /// Gets or sets the text content of this part.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
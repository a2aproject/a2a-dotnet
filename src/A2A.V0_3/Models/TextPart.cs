using System.Text.Json.Serialization;

namespace A2A.V0_3;

/// <summary>
/// Represents a text segment within parts.
/// </summary>
public sealed class TextPart() : Part(PartKind.Text)
{
    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    [JsonPropertyName("text")]
    [JsonRequired]
    public string Text { get; set; } = string.Empty;
}
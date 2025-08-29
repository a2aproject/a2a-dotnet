using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a text segment within parts.
/// </summary>
public sealed class TextPart : Part
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextPart"/> class.
    /// </summary>
    public TextPart() : base(PartKind.Text)
    {
    }
    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    [JsonPropertyName("text")]
    [JsonRequired]
    public string Text { get; set; } = string.Empty;
}
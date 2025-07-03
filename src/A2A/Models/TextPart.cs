using System.Text.Json.Serialization;

namespace A2A;

public class TextPart : Part
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
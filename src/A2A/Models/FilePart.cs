using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a File segment within parts.
/// </summary>
public class FilePart : Part
{
    /// <summary>
    /// File content either as url or bytes.
    /// </summary>
    [JsonPropertyName("file")]
    public FileWithBytes File { get; set; } = new FileWithBytes();
}
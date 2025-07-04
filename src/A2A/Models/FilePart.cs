using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a file-based part of a message or content.
/// </summary>
public class FilePart : Part
{
    /// <summary>
    /// Gets or sets the file content associated with this part.
    /// </summary>
    [JsonPropertyName("file")]
    public FileWithBytes File { get; set; } = new FileWithBytes();
}
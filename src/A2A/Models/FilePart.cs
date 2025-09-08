using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a File segment within parts.
/// </summary>
public sealed class FilePart() : Part(PartKind.File)
{
    /// <summary>
    /// File content either as url or bytes.
    /// </summary>
    [JsonPropertyName("file")]
    [JsonRequired]
    public FileContent File { get; set; } = new FileWithBytes();
}
using System.Text.Json.Serialization;

namespace A2A.V0_3;

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
    required public FileContent File { get; set; }
}
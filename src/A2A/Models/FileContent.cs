using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents the base entity for FileParts.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FileWithBytes), "bytes")]
[JsonDerivedType(typeof(FileWithUri), "uri")]
public class FileContent
{
    /// <summary>
    /// Optional metadata for the file.
    /// </summary>
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];
}

/// <summary>
/// Define the variant where 'bytes' is present and 'uri' is absent.
/// </summary>
public class FileWithBytes : FileContent
{
    /// <summary>
    /// Optional name for the file.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Optional mimeType for the file.
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// base64 encoded content of the file.
    /// </summary>
    [JsonPropertyName("bytes")]
    public string? Bytes { get; set; }

    /// <summary>
    /// URL for the File content.
    /// </summary>
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }
}

/// <summary>
/// Define the variant where 'uri' is present and 'bytes' is absent.
/// </summary>
public class FileWithUri : FileContent
{
    /// <summary>
    /// Optional name for the file.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Optional mimeType for the file.
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// URL for the File content.
    /// </summary>
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }
}
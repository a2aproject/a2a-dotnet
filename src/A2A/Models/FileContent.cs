using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents the base entity for FileParts.
/// According to the A2A spec, FileContent types are distinguished by the presence
/// of either "bytes" or "uri" properties, not by a discriminator.
/// </summary>
[JsonConverter(typeof(FileContentConverter))]
[JsonDerivedType(typeof(FileWithBytes))]
[JsonDerivedType(typeof(FileWithUri))]
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
public sealed class FileWithBytes : FileContent
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
    [JsonRequired]
    public string Bytes { get; set; } = string.Empty;

    /// <summary>
    /// URL for the File content.
    /// </summary>
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }
}

/// <summary>
/// Define the variant where 'uri' is present and 'bytes' is absent.
/// </summary>
public sealed class FileWithUri : FileContent
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
    [JsonRequired]
    public string Uri { get; set; } = string.Empty;
}

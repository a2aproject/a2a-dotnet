using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents base file content with polymorphic JSON serialization support.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FileWithBytes), "bytes")]
[JsonDerivedType(typeof(FileWithUri), "uri")]
public class FileContent
{
    /// <summary>
    /// Gets or sets additional metadata associated with the file.
    /// </summary>
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];
}

/// <summary>
/// Represents file content that includes the actual file data as bytes.
/// </summary>
public class FileWithBytes : FileContent
{
    /// <summary>
    /// Gets or sets the name of the file.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the MIME type of the file.
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets the file content as a base64-encoded string.
    /// </summary>
    [JsonPropertyName("bytes")]
    public string? Bytes { get; set; }

    /// <summary>
    /// Gets or sets an optional URI reference for the file.
    /// </summary>
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }
}

/// <summary>
/// Represents file content that references a file by URI rather than including the actual data.
/// </summary>
public class FileWithUri : FileContent
{
    /// <summary>
    /// Gets or sets the name of the file.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the MIME type of the file.
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets the URI where the file can be accessed.
    /// </summary>
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }
}
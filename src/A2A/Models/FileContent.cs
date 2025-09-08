using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents the base entity for FileParts.
/// </summary>
/// <param name="kind">The <c>kind</c> discriminator value</param>
[JsonConverter(typeof(FileContentConverterViaKindDiscriminator<FileContent>))]
[JsonDerivedType(typeof(FileWithBytes))]
[JsonDerivedType(typeof(FileWithUri))]
// You might be wondering why we don't use JsonPolymorphic here. The reason is that it automatically throws a NotSupportedException if the 
// discriminator isn't present or accounted for. In the case of A2A, we want to throw a more specific A2AException with an error code, so
// we implement our own converter to handle that, with the discriminator logic implemented by-hand.
public class FileContent(string kind)
{
    /// <summary>
    /// The 'kind' discriminator value
    /// </summary>
    [JsonRequired, JsonPropertyName(BaseKindDiscriminatorConverter<FileContent>.DiscriminatorPropertyName), JsonInclude, JsonPropertyOrder(int.MinValue)]
    public string Kind { get; internal set; } = kind;
    /// <summary>
    /// Optional metadata for the file.
    /// </summary>
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];
}

/// <summary>
/// Define the variant where 'bytes' is present and 'uri' is absent.
/// </summary>
public sealed class FileWithBytes() : FileContent(FileContentKind.Bytes)
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
public sealed class FileWithUri() : FileContent(FileContentKind.Uri)
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

internal class FileContentConverterViaKindDiscriminator<T> : BaseKindDiscriminatorConverter<T> where T : FileContent
{
    protected override IReadOnlyDictionary<string, Type> KindToTypeMapping { get; } = new Dictionary<string, Type>
    {
        [FileContentKind.Bytes] = typeof(FileWithBytes),
        [FileContentKind.Uri] = typeof(FileWithUri)
    };

    protected override string DisplayName { get; } = "file content";
}
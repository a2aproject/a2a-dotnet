using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>
/// Defines the set of FileContent kinds used as the 'kind' discriminator in serialized payloads.
/// </summary>
/// <remarks>
/// Values are serialized as lowercase kebab-case strings via <see cref="KebabCaseLowerJsonStringEnumConverter{TEnum}"/>.
/// </remarks>
[JsonConverter(typeof(KebabCaseLowerJsonStringEnumConverter<FileContentKind>))]
public enum FileContentKind
{
    /// <summary>
    /// A file content containing bytes.
    /// </summary>
    /// <seealso cref="FileWithBytes"/>
    Bytes,

    /// <summary>
    /// A file content containing a URI.
    /// </summary>
    /// <seealso cref="FileWithUri"/>
    Uri
}

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
public class FileContent(FileContentKind kind)
{
    /// <summary>
    /// The 'kind' discriminator value
    /// </summary>
    [JsonRequired, JsonPropertyName(BaseKindDiscriminatorConverter<FileContent, FileContentKind>.DiscriminatorPropertyName), JsonInclude, JsonPropertyOrder(int.MinValue)]
    public FileContentKind Kind { get; internal set; } = kind;
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

internal class FileContentConverterViaKindDiscriminator<T> : BaseKindDiscriminatorConverter<T, FileContentKind> where T : FileContent
{
    protected override Type[] TypeMapping { get; } =
    [
        typeof(FileWithBytes),   // FileContentKind.Bytes = 0
        typeof(FileWithUri)      // FileContentKind.Uri = 1
    ];

    protected override string DisplayName { get; } = "file content";

    protected override FileContentKind DeserializeKind(JsonElement kindProp) =>
        kindProp.Deserialize(A2AJsonUtilities.JsonContext.Default.FileContentKind);
}
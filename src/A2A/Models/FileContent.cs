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
    [JsonRequired, JsonPropertyName(FileContentConverterViaKindDiscriminator<FileContent>.DiscriminatorPropertyName), JsonInclude, JsonPropertyOrder(int.MinValue)]
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

internal class FileContentConverterViaKindDiscriminator<T> : JsonConverter<T> where T : FileContent
{
    internal const string DiscriminatorPropertyName = "kind";

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (!root.TryGetProperty(DiscriminatorPropertyName, out var kindProp) || kindProp.ValueKind is not JsonValueKind.String)
        {
            throw new A2AException($"Missing required '{DiscriminatorPropertyName}' discriminator for {typeof(T).Name}.", A2AErrorCode.InvalidRequest);
        }

        T? fileContentObj = null;
        Exception? deserializationException = null;
        try
        {
            var kindValue = kindProp.Deserialize(A2AJsonUtilities.JsonContext.Default.FileContentKind);
#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
            // We don't need to handle this because the previous Deserialize call would have thrown if the value was invalid.
            JsonTypeInfo typeInfo = kindValue switch
            {
                FileContentKind.Bytes => options.GetTypeInfo(typeof(FileWithBytes)),
                FileContentKind.Uri => options.GetTypeInfo(typeof(FileWithUri)),
            };
#pragma warning restore CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

            fileContentObj = (T?)root.Deserialize(typeInfo);
        }
        catch (Exception e)
        {
            deserializationException = e;
        }

        if (deserializationException is not null || fileContentObj is null)
        {
            throw new A2AException($"Failed to deserialize {kindProp.GetString()} file content", deserializationException, A2AErrorCode.InvalidRequest);
        }

        return fileContentObj;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var element = JsonSerializer.SerializeToElement(value, options.GetTypeInfo(value.GetType()));
        writer.WriteStartObject();

        foreach (var prop in element.EnumerateObject())
        {
            prop.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
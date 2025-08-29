using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>
/// Defines the set of Part kinds used as the 'kind' discriminator in serialized payloads.
/// </summary>
/// <remarks>
/// Values are serialized as lowercase kebab-case strings via <see cref="KebabCaseLowerJsonStringEnumConverter{TEnum}"/>.
/// </remarks>
[JsonConverter(typeof(KebabCaseLowerJsonStringEnumConverter<PartKind>))]
public enum PartKind
{
    /// <summary>
    /// A text part containing plain textual content.
    /// </summary>
    /// <seealso cref="TextPart"/>
    Text,

    /// <summary>
    /// A file part containing file content.
    /// </summary>
    /// <seealso cref="FilePart"/>
    File,

    /// <summary>
    /// A data part containing structured JSON data.
    /// </summary>
    /// <seealso cref="DataPart"/>
    Data
}

/// <summary>
/// Represents a part of a message, which can be text, a file, or structured data.
/// </summary>
/// <param name="kind">The <c>kind</c> discriminator value</param>
[JsonConverter(typeof(PartConverterViaKindDiscriminator<Part>))]
[JsonDerivedType(typeof(TextPart))]
[JsonDerivedType(typeof(FilePart))]
[JsonDerivedType(typeof(DataPart))]
// You might be wondering why we don't use JsonPolymorphic here. The reason is that it automatically throws a NotSupportedException if the 
// discriminator isn't present or accounted for. In the case of A2A, we want to throw a more specific A2AException with an error code, so
// we implement our own converter to handle that, with the discriminator logic implemented by-hand.
public abstract class Part(PartKind kind)
{
    /// <summary>
    /// The 'kind' discriminator value
    /// </summary>
    [JsonRequired, JsonPropertyName(PartConverterViaKindDiscriminator<Part>.DiscriminatorPropertyName), JsonInclude, JsonPropertyOrder(int.MinValue)]
    public PartKind Kind { get; internal set; } = kind;
    /// <summary>
    /// Optional metadata associated with the part.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    /// <summary>
    /// Casts this part to a TextPart.
    /// </summary>
    /// <returns>The part as a TextPart.</returns>
    /// <exception cref="InvalidCastException">Thrown when the part is not a TextPart.</exception>
    public TextPart AsTextPart() => this is TextPart textPart ?
        textPart :
        throw new InvalidCastException($"Cannot cast {GetType().Name} to TextPart.");

    /// <summary>
    /// Casts this part to a FilePart.
    /// </summary>
    /// <returns>The part as a FilePart.</returns>
    /// <exception cref="InvalidCastException">Thrown when the part is not a FilePart.</exception>
    public FilePart AsFilePart() => this is FilePart filePart ?
        filePart :
        throw new InvalidCastException($"Cannot cast {GetType().Name} to FilePart.");

    /// <summary>
    /// Casts this part to a DataPart.
    /// </summary>
    /// <returns>The part as a DataPart.</returns>
    /// <exception cref="InvalidCastException">Thrown when the part is not a DataPart.</exception>
    public DataPart AsDataPart() => this is DataPart dataPart ?
        dataPart :
        throw new InvalidCastException($"Cannot cast {GetType().Name} to DataPart.");
}

internal class PartConverterViaKindDiscriminator<T> : JsonConverter<T> where T : Part
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

        T? partObj = null;
        Exception? deserializationException = null;
        try
        {
            var kindValue = kindProp.Deserialize(A2AJsonUtilities.JsonContext.Default.PartKind);
#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
            // We don't need to handle this because the previous Deserialize call would have thrown if the value was invalid.
            JsonTypeInfo typeInfo = kindValue switch
            {
                PartKind.Text => options.GetTypeInfo(typeof(TextPart)),
                PartKind.File => options.GetTypeInfo(typeof(FilePart)),
                PartKind.Data => options.GetTypeInfo(typeof(DataPart)),
            };
#pragma warning restore CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

            partObj = (T?)root.Deserialize(typeInfo);
        }
        catch (Exception e)
        {
            deserializationException = e;
        }

        if (deserializationException is not null || partObj is null)
        {
            throw new A2AException($"Failed to deserialize {kindProp.GetString()} part", deserializationException, A2AErrorCode.InvalidRequest);
        }

        return partObj;
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
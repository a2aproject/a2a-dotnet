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
    /// Unknown value, used for unrecognized values.
    /// </summary>
    Unknown = 0,

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
    Data,

    /// <summary>
    /// Helper value to track the number of enum values when used as array indices. This must always be the last value in the enumeration.
    /// </summary>
    Count
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
    [JsonRequired, JsonPropertyName(BaseKindDiscriminatorConverter<Part, PartKind>.DiscriminatorPropertyName), JsonInclude, JsonPropertyOrder(int.MinValue)]
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

internal class PartConverterViaKindDiscriminator<T> : BaseKindDiscriminatorConverter<T, PartKind> where T : Part
{
    protected override Type?[] TypeMapping { get; } =
    [
        null,
        typeof(TextPart),   // PartKind.Text = 1
        typeof(FilePart),   // PartKind.File = 2
        typeof(DataPart)    // PartKind.Data = 3
    ];

    protected override string DisplayName { get; } = "part";

    protected override bool TryDeserializeKind(JsonElement kindProp, out PartKind value)
    {
        value = PartKind.Unknown;
        try
        {
            value = kindProp.Deserialize(A2AJsonUtilities.JsonContext.Default.PartKind);
            return value is not PartKind.Unknown;
        }
        catch
        {
            return false;
        }
    }
}
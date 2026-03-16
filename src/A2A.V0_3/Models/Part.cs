using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.V0_3;

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
public abstract class Part(string kind)
{
    /// <summary>
    /// The 'kind' discriminator value
    /// </summary>
    [JsonRequired, JsonPropertyName(BaseKindDiscriminatorConverter<Part>.DiscriminatorPropertyName), JsonInclude, JsonPropertyOrder(int.MinValue)]
    public string Kind { get; internal set; } = kind;
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

internal class PartConverterViaKindDiscriminator<T> : BaseKindDiscriminatorConverter<T> where T : Part
{
    protected override IReadOnlyDictionary<string, Type> KindToTypeMapping { get; } = new Dictionary<string, Type>
    {
        [PartKind.Text] = typeof(TextPart),
        [PartKind.File] = typeof(FilePart),
        [PartKind.Data] = typeof(DataPart)
    };

    protected override string DisplayName { get; } = "part";
}
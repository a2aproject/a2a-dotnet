using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a part of a message, which can be text, a file, or structured data.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextPart), "text")]
[JsonDerivedType(typeof(FilePart), "file")]
[JsonDerivedType(typeof(DataPart), "data")]
public abstract class Part
{
    /// <summary>
    /// Optional metadata associated with the part.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    /// <summary>
    /// Casts this part to a TextPart.
    /// </summary>
    /// <returns>The part as a TextPart</returns>
    /// <exception cref="InvalidCastException">Thrown when the part is not a TextPart</exception>
    public TextPart AsTextPart() => this is TextPart textPart ?
        textPart :
        throw new InvalidCastException($"Cannot cast {GetType().Name} to TextPart.");

    /// <summary>
    /// Casts this part to a FilePart.
    /// </summary>
    /// <returns>The part as a FilePart</returns>
    /// <exception cref="InvalidCastException">Thrown when the part is not a FilePart</exception>
    public FilePart AsFilePart() => this is FilePart filePart ?
        filePart :
        throw new InvalidCastException($"Cannot cast {GetType().Name} to FilePart.");

    /// <summary>
    /// Casts this part to a DataPart.
    /// </summary>
    /// <returns>The part as a DataPart</returns>
    /// <exception cref="InvalidCastException">Thrown when the part is not a DataPart</exception>
    public DataPart AsDataPart() => this is DataPart dataPart ?
        dataPart :
        throw new InvalidCastException($"Cannot cast {GetType().Name} to DataPart.");
}
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents an abstract base class for different types of content parts in A2A messages with polymorphic JSON serialization support.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextPart), "text")]
[JsonDerivedType(typeof(FilePart), "file")]
[JsonDerivedType(typeof(DataPart), "data")]
public abstract class Part
{
    /// <summary>
    /// Gets or sets additional metadata associated with this part.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    /// <summary>
    /// Safely casts this part to a TextPart.
    /// </summary>
    /// <returns>This part as a TextPart.</returns>
    /// <exception cref="InvalidCastException">Thrown when this part is not a TextPart.</exception>
    public TextPart AsTextPart() => this is TextPart textPart ?
        textPart :
        throw new InvalidCastException($"Cannot cast {GetType().Name} to TextPart.");

    /// <summary>
    /// Safely casts this part to a FilePart.
    /// </summary>
    /// <returns>This part as a FilePart.</returns>
    /// <exception cref="InvalidCastException">Thrown when this part is not a FilePart.</exception>
    public FilePart AsFilePart() => this is FilePart filePart ?
        filePart :
        throw new InvalidCastException($"Cannot cast {GetType().Name} to FilePart.");

    /// <summary>
    /// Safely casts this part to a DataPart.
    /// </summary>
    /// <returns>This part as a DataPart.</returns>
    /// <exception cref="InvalidCastException">Thrown when this part is not a DataPart.</exception>
    public DataPart AsDataPart() => this is DataPart dataPart ?
        dataPart :
        throw new InvalidCastException($"Cannot cast {GetType().Name} to DataPart.");
}
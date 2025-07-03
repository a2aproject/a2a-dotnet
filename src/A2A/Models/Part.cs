using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextPart), "text")]
[JsonDerivedType(typeof(FilePart), "file")]
[JsonDerivedType(typeof(DataPart), "data")]
public abstract class Part
{
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    public TextPart AsTextPart() => this is TextPart textPart ?
        textPart :
        throw new InvalidCastException($"Cannot cast {GetType().Name} to TextPart.");

    public FilePart AsFilePart() => this is FilePart filePart ?
        filePart :
        throw new InvalidCastException($"Cannot cast {GetType().Name} to FilePart.");

    public DataPart AsDataPart() => this is DataPart dataPart ?
        dataPart :
        throw new InvalidCastException($"Cannot cast {GetType().Name} to DataPart.");
}
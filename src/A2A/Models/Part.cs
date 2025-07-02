using System.ComponentModel.DataAnnotations;
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

    public TextPart AsTextPart()
    {
        if (this is TextPart textPart)
        {
            return textPart;
        }
        throw new InvalidCastException($"Cannot cast {this.GetType().Name} to TextPart.");
    }

    public FilePart AsFilePart()
    {
        if (this is FilePart filePart)
        {
            return filePart;
        }
        throw new InvalidCastException($"Cannot cast {this.GetType().Name} to FilePart.");
    }

    public DataPart AsDataPart()
    {
        if (this is DataPart dataPart)
        {
            return dataPart;
        }
        throw new InvalidCastException($"Cannot cast {this.GetType().Name} to DataPart.");
    }
}



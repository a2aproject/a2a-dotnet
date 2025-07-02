using System.Text.Json.Serialization;

namespace A2A;

public class FilePart : Part
{
    [JsonPropertyName("file")]
    public FileWithBytes File { get; set; } = new FileWithBytes();
}



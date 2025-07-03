using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FileWithBytes), "bytes")]
[JsonDerivedType(typeof(FileWithUri), "uri")]
public class FileContent
{
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];
}

public class FileWithBytes : FileContent
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("bytes")]
    public string? Bytes { get; set; }

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }
}

public class FileWithUri : FileContent
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }
}
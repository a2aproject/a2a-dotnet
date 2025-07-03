using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

public class DataPart : Part
{
    [JsonPropertyName("data")]
    public Dictionary<string, JsonElement> Data { get; set; } = [];
}



using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.Core;

public class TaskIdParams
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}



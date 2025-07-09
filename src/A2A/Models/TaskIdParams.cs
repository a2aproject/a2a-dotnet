using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Parameters containing only a task ID, used for simple task operations.
/// </summary>
public class TaskIdParams
{
    /// <summary>
    /// Task id.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Extension metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}
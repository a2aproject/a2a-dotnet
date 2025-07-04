using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents parameters containing a task identifier and optional metadata.
/// </summary>
public class TaskIdParams
{
    /// <summary>
    /// Gets or sets the unique identifier of the task.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets optional metadata associated with the task.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}
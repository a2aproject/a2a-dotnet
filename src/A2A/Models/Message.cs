namespace A2A;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Represents a message in the A2A protocol.</summary>
public sealed class Message
{
    /// <summary>Gets or sets the role of the message sender.</summary>
    [JsonPropertyName("role"), JsonRequired]
    public Role Role { get; set; }

    /// <summary>Gets or sets the parts of this message.</summary>
    [JsonPropertyName("parts"), JsonRequired]
    public List<Part> Parts { get; set; } = [];

    /// <summary>Unique identifier for the message.</summary>
    [JsonPropertyName("messageId"), JsonRequired]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Gets or sets the context identifier.</summary>
    [JsonPropertyName("contextId")]
    public string? ContextId { get; set; }

    /// <summary>Gets or sets the task identifier.</summary>
    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    /// <summary>Gets or sets the list of referenced task identifiers.</summary>
    [JsonPropertyName("referenceTaskIds")]
    public List<string>? ReferenceTaskIds { get; set; }

    /// <summary>Gets or sets the extensions associated with this message.</summary>
    [JsonPropertyName("extensions")]
    public List<string>? Extensions { get; set; }

    /// <summary>Gets or sets the metadata associated with this message.</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

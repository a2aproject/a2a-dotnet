namespace A2A;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Represents a message in the A2A protocol.</summary>
public sealed class Message
{
    /// <summary>Gets or sets the role of the message sender.</summary>
    [JsonRequired]
    public Role Role { get; set; }

    /// <summary>Gets or sets the parts of this message.</summary>
    [JsonRequired]
    public List<Part> Parts { get; set; } = [];

    /// <summary>Unique identifier for the message.</summary>
    [JsonRequired]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Gets or sets the context identifier.</summary>
    public string? ContextId { get; set; }

    /// <summary>Gets or sets the task identifier.</summary>
    public string? TaskId { get; set; }

    /// <summary>Gets or sets the list of referenced task identifiers.</summary>
    public List<string>? ReferenceTaskIds { get; set; }

    /// <summary>Gets or sets the extensions associated with this message.</summary>
    public List<string>? Extensions { get; set; }

    /// <summary>Gets or sets the metadata associated with this message.</summary>
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

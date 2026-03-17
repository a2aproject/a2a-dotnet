namespace A2A;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Represents a task in the A2A protocol.</summary>
public sealed class AgentTask
{
    /// <summary>Gets or sets the unique task identifier.</summary>
    [JsonRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the context identifier.</summary>
    [JsonRequired]
    public string ContextId { get; set; } = string.Empty;

    /// <summary>Gets or sets the current status of the task.</summary>
    [JsonRequired]
    public TaskStatus Status { get; set; } = new();

    /// <summary>Gets or sets the history of messages for this task.</summary>
    public List<Message>? History { get; set; }

    /// <summary>Gets or sets the artifacts produced by this task.</summary>
    public List<Artifact>? Artifacts { get; set; }

    /// <summary>Gets or sets the metadata associated with this task.</summary>
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

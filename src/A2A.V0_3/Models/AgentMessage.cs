using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.V0_3;

/// <summary>
/// Message sender's role.
/// </summary>
[JsonConverter(typeof(KebabCaseLowerJsonStringEnumConverter<MessageRole>))]
public enum MessageRole
{
    /// <summary>
    /// User role.
    /// </summary>
    User,
    /// <summary>
    /// Agent role.
    /// </summary>
    Agent
}

/// <summary>
/// Represents a single message exchanged between user and agent.
/// </summary>
public sealed class AgentMessage() : A2AResponse(A2AEventKind.Message)
{
    /// <summary>
    /// Message sender's role.
    /// </summary>
    [JsonPropertyName("role")]
    [JsonRequired]
    public MessageRole Role { get; set; } = MessageRole.User;

    /// <summary>
    /// Message content.
    /// </summary>
    [JsonPropertyName("parts")]
    [JsonRequired]
    public List<Part> Parts { get; set; } = [];

    /// <summary>
    /// Extension metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    /// <summary>
    /// List of tasks referenced as context by this message.
    /// </summary>
    [JsonPropertyName("referenceTaskIds")]
    public List<string>? ReferenceTaskIds { get; set; }

    /// <summary>
    /// Identifier created by the message creator.
    /// </summary>
    /// <remarks>
    /// Not <c>[JsonRequired]</c>: some v0.3 producers omit it (a discrepancy between the
    /// <c>.proto</c> and JSON Schema definitions of v0.3). Defaults to a freshly generated id
    /// rather than an empty string, so a message deserialized without one still has a usable,
    /// unique identifier.
    /// </remarks>
    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Identifier of task the message is related to.
    /// </summary>
    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    /// <summary>
    /// The context the message is associated with.
    /// </summary>
    [JsonPropertyName("contextId")]
    public string? ContextId { get; set; }

    /// <summary>
    /// The URIs of extensions that are present or contributed to this Message.
    /// </summary>
    [JsonPropertyName("extensions")]
    public List<string>? Extensions { get; set; }
}
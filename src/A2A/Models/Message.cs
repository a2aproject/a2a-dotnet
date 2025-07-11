using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

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
/// JSON converter for MessageRole enum.
/// </summary>
public sealed class MessageRoleConverter : JsonConverter<MessageRole>
{
    /// <inheritdoc />
    public override MessageRole Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value switch
        {
            "user" => MessageRole.User,
            "agent" => MessageRole.Agent,
            _ => throw new JsonException($"Unknown message role: {value}")
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, MessageRole value, JsonSerializerOptions options)
    {
        var role = value switch
        {
            MessageRole.User => "user",
            MessageRole.Agent => "agent",
            _ => throw new JsonException($"Unknown message role: {value}")
        };
        writer.WriteStringValue(role);
    }
}

/// <summary>
/// Represents a single message exchanged between user and agent.
/// </summary>
public sealed class Message : A2AResponse
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
    [JsonPropertyName("messageId")]
    [JsonRequired]
    public string MessageId { get; set; } = string.Empty;

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
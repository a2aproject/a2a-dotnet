using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents the role of a message sender in A2A communication.
/// </summary>
[JsonConverter(typeof(MessageRoleConverter))]
public enum MessageRole
{
    /// <summary>
    /// Message from a user.
    /// </summary>
    User,
    /// <summary>
    /// Message from an agent.
    /// </summary>
    Agent
}

/// <summary>
/// Provides JSON serialization and deserialization for MessageRole enumeration values.
/// </summary>
public class MessageRoleConverter : JsonConverter<MessageRole>
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
/// Represents a message in A2A communication containing content parts and metadata.
/// </summary>
public class Message : A2AResponse
{
    /// <summary>
    /// Gets or sets the role of the message sender.
    /// </summary>
    [JsonPropertyName("role")]
    [JsonRequired]
    public MessageRole Role { get; set; } = MessageRole.User;

    /// <summary>
    /// Gets or sets the content parts that make up the message.
    /// </summary>
    [JsonPropertyName("parts")]
    [JsonRequired]
    public List<Part> Parts { get; set; } = [];

    /// <summary>
    /// Gets or sets additional metadata associated with the message.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    /// <summary>
    /// Gets or sets the list of task IDs that this message references.
    /// </summary>
    [JsonPropertyName("referenceTaskIds")]
    public List<string>? ReferenceTaskIds { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier for this message.
    /// </summary>
    [JsonPropertyName("messageId")]
    [JsonRequired]
    public string? MessageId { get; set; }

    /// <summary>
    /// Gets or sets the task identifier that this message is associated with.
    /// </summary>
    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    /// <summary>
    /// Gets or sets the context identifier that groups related messages together.
    /// </summary>
    [JsonPropertyName("contextId")]
    public string? ContextId { get; set; }
}
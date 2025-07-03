using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

[JsonConverter(typeof(MessageRoleConverter))]
public enum MessageRole
{
    User,
    Agent
}

public class MessageRoleConverter : JsonConverter<MessageRole>
{
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

public class Message : A2AResponse
{
    [JsonPropertyName("role")]
    [JsonRequired]
    public MessageRole Role { get; set; } = MessageRole.User;

    [JsonPropertyName("parts")]
    [JsonRequired]
    public List<Part> Parts { get; set; } = [];

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    [JsonPropertyName("referenceTaskIds")]
    public List<string>? ReferenceTaskIds { get; set; }

    [JsonPropertyName("messageId")]
    [JsonRequired]
    public string? MessageId { get; set; }

    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("contextId")]
    public string? ContextId { get; set; }
}
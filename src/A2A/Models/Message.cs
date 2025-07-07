using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

[JsonConverter(typeof(KebabCaseLowerJsonStringEnumConverter<MessageRole>))]
public enum MessageRole
{
    User,
    Agent
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
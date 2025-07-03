using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

public class AgentTask : A2AResponse
{
    [JsonPropertyName("id")]
    [JsonRequired]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("contextId")]
    [JsonRequired]
    public string? ContextId { get; set; }

    [JsonPropertyName("status")]
    [JsonRequired]
    public AgentTaskStatus Status { get; set; } = new AgentTaskStatus();

    [JsonPropertyName("artifacts")]
    public List<Artifact>? Artifacts { get; set; }

    [JsonPropertyName("history")]
    public List<Message>? History { get; set; } = [];

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}
using System.Text.Json.Serialization;

namespace A2A;

public class TaskArtifactUpdateEvent : TaskUpdateEvent
{
    [JsonPropertyName("artifact")]
    public Artifact Artifact { get; set; } = new Artifact();

    [JsonPropertyName("append")]
    public bool? Append { get; set; }

    [JsonPropertyName("lastChunk")]
    public bool? LastChunk { get; set; }

}



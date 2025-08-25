using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Base class for A2A events.
/// </summary>
/// <param name="kind">The event kind discriminator.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TaskStatusUpdateEvent), TaskStatusUpdateEvent.KindValue)]
[JsonDerivedType(typeof(TaskArtifactUpdateEvent), TaskArtifactUpdateEvent.KindValue)]
[JsonDerivedType(typeof(Message), Message.KindValue)]
[JsonDerivedType(typeof(AgentTask), AgentTask.KindValue)]
public abstract class A2AEvent(string kind)
{
    /// <summary>
    /// Gets the event kind discriminator used for polymorphic serialization.
    /// </summary>
    [JsonIgnore]
    public string Kind { get; } = kind;
}

/// <summary>
/// A2A response objects.
/// </summary>
/// <param name="kind">The event kind discriminator.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Message), Message.KindValue)]
[JsonDerivedType(typeof(AgentTask), AgentTask.KindValue)]
public abstract class A2AResponse(string kind) : A2AEvent(kind);
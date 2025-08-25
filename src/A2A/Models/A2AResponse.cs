using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Base class for A2A events.
/// </summary>
/// <remarks>
/// This class is used as the base for all event types in the A2A protocol. It supports polymorphic serialization
/// using the <c>kind</c> discriminator property.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TaskStatusUpdateEvent), TaskStatusUpdateEvent.KindValue)]
[JsonDerivedType(typeof(TaskArtifactUpdateEvent), TaskArtifactUpdateEvent.KindValue)]
[JsonDerivedType(typeof(Message), Message.KindValue)]
[JsonDerivedType(typeof(AgentTask), AgentTask.KindValue)]
public abstract class A2AEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="A2AEvent"/> class.
    /// </summary>
    /// <param name="kind">The event kind discriminator used for polymorphic serialization.</param>
    protected internal A2AEvent(string kind) => this.Kind = kind;

    /// <summary>
    /// Gets the event kind discriminator used for polymorphic serialization.
    /// </summary>
    [JsonIgnore]
    public string Kind { get; }
}

/// <summary>
/// A2A response objects.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Message), Message.KindValue)]
[JsonDerivedType(typeof(AgentTask), AgentTask.KindValue)]
public abstract class A2AResponse: A2AEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="A2AResponse"/> class.
    /// </summary>
    /// <param name="kind">The event kind discriminator used for polymorphic serialization.</param>
    protected internal A2AResponse(string kind) : base(kind) { }
}
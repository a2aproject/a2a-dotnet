using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a base class for all A2A events with polymorphic JSON serialization support.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TaskStatusUpdateEvent), "status-update")]
[JsonDerivedType(typeof(TaskArtifactUpdateEvent), "artifact-update")]
[JsonDerivedType(typeof(Message), "message")]
[JsonDerivedType(typeof(AgentTask), "task")]
public class A2AEvent
{
}

/// <summary>
/// Represents a base class for A2A responses with polymorphic JSON serialization support.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Message), "message")]
[JsonDerivedType(typeof(AgentTask), "task")]
public class A2AResponse : A2AEvent
{
}
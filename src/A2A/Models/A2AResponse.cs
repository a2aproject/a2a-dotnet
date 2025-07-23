using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Base class for A2A events.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TaskStatusUpdateEvent), "status-update")]
[JsonDerivedType(typeof(TaskArtifactUpdateEvent), "artifact-update")]
[JsonDerivedType(typeof(Message), "message")]
[JsonDerivedType(typeof(AgentTask), "task")]
[JsonDerivedType(typeof(TrimmedAgentTask))]
public abstract class A2AEvent
{
}

/// <summary>
/// A2A response objects.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Message), "message")]
[JsonDerivedType(typeof(AgentTask), "task")]
[JsonDerivedType(typeof(TrimmedAgentTask))]
public abstract class A2AResponse : A2AEvent
{
}
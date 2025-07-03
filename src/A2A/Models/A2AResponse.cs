using System.Text.Json.Serialization;

namespace A2A;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TaskStatusUpdateEvent), "status-update")]
[JsonDerivedType(typeof(TaskArtifactUpdateEvent), "artifact-update")]
[JsonDerivedType(typeof(Message), "message")]
[JsonDerivedType(typeof(AgentTask), "task")]
public class A2AEvent
{
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Message), "message")]
[JsonDerivedType(typeof(AgentTask), "task")]
public class A2AResponse : A2AEvent
{
}
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>
/// Defines the set of A2A event kinds used as the 'kind' discriminator in serialized payloads.
/// </summary>
/// <remarks>
/// Values are serialized as lowercase kebab-case strings via <see cref="KebabCaseLowerJsonStringEnumConverter{TEnum}"/>.
/// </remarks>
[JsonConverter(typeof(KebabCaseLowerJsonStringEnumConverter<A2AEventKind>))]
public enum A2AEventKind
{
    /// <summary>
    /// A conversational message from an agent.
    /// </summary>
    /// <seealso cref="AgentMessage"/>
    Message,

    /// <summary>
    /// A task issued to or produced by an agent.
    /// </summary>
    /// <seealso cref="AgentTask"/>
    Task,

    /// <summary>
    /// An update describing the current state of a task execution.
    /// </summary>
    /// <seealso cref="TaskStatusUpdateEvent"/>
    StatusUpdate,

    /// <summary>
    /// A notification that artifacts associated with a task have changed.
    /// </summary>
    /// <seealso cref="TaskArtifactUpdateEvent"/>
    ArtifactUpdate
}

/// <summary>
/// Base class for A2A events.
/// </summary>
/// <param name="kind">The <c>kind</c> discriminator value</param>
[JsonConverter(typeof(A2AEventConverterViaKindDiscriminator<A2AEvent>))]
[JsonDerivedType(typeof(TaskStatusUpdateEvent))]
[JsonDerivedType(typeof(TaskArtifactUpdateEvent))]
[JsonDerivedType(typeof(AgentMessage))]
[JsonDerivedType(typeof(AgentTask))]
// You might be wondering why we don't use JsonPolymorphic here. The reason is that it automatically throws a NotSupportedException if the 
// discriminator isn't present or accounted for. In the case of A2A, we want to throw a more specific A2AException with an error code, so
// we implement our own converter to handle that, with the discriminator logic implemented by-hand.
public abstract class A2AEvent(A2AEventKind kind)
{
    /// <summary>
    /// The 'kind' discriminator value
    /// </summary>
    [JsonRequired, JsonPropertyName(BaseKindDiscriminatorConverter<A2AEvent, A2AEventKind>.DiscriminatorPropertyName), JsonInclude, JsonPropertyOrder(int.MinValue)]
    public A2AEventKind Kind { get; internal set; } = kind;
}

/// <summary>
/// A2A response objects.
/// </summary>
/// <param name="kind">The <c>kind</c> discriminator value</param>
[JsonConverter(typeof(A2AEventConverterViaKindDiscriminator<A2AResponse>))]
[JsonDerivedType(typeof(AgentMessage))]
[JsonDerivedType(typeof(AgentTask))]
// You might be wondering why we don't use JsonPolymorphic here. The reason is that it automatically throws a NotSupportedException if the 
// discriminator isn't present or accounted for. In the case of A2A, we want to throw a more specific A2AException with an error code, so
// we implement our own converter to handle that, with the discriminator logic implemented by-hand.
public abstract class A2AResponse(A2AEventKind kind) : A2AEvent(kind);

internal class A2AEventConverterViaKindDiscriminator<T> : BaseKindDiscriminatorConverter<T, A2AEventKind> where T : A2AEvent
{
    protected override Type[] TypeMapping { get; } =
    [
        typeof(AgentMessage),           // A2AEventKind.Message = 0
        typeof(AgentTask),              // A2AEventKind.Task = 1
        typeof(TaskStatusUpdateEvent),  // A2AEventKind.StatusUpdate = 2
        typeof(TaskArtifactUpdateEvent) // A2AEventKind.ArtifactUpdate = 3
    ];

    protected override string DisplayName => "event";

    protected override A2AEventKind DeserializeKind(JsonElement kindProp) =>
        kindProp.Deserialize(A2AJsonUtilities.JsonContext.Default.A2AEventKind);
}

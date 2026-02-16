using System.Text.Json.Serialization;

namespace A2A.V0_3;

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
public abstract class A2AEvent(string kind)
{
    /// <summary>
    /// The 'kind' discriminator value
    /// </summary>
    [JsonRequired, JsonPropertyName(BaseKindDiscriminatorConverter<A2AEvent>.DiscriminatorPropertyName), JsonInclude, JsonPropertyOrder(int.MinValue)]
    public string Kind { get; internal set; } = kind;
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
public abstract class A2AResponse(string kind) : A2AEvent(kind);

internal class A2AEventConverterViaKindDiscriminator<T> : BaseKindDiscriminatorConverter<T> where T : A2AEvent
{
    protected override IReadOnlyDictionary<string, Type> KindToTypeMapping { get; } = new Dictionary<string, Type>
    {
        [A2AEventKind.Message] = typeof(AgentMessage),
        [A2AEventKind.Task] = typeof(AgentTask),
        [A2AEventKind.StatusUpdate] = typeof(TaskStatusUpdateEvent),
        [A2AEventKind.ArtifactUpdate] = typeof(TaskArtifactUpdateEvent)
    };

    protected override string DisplayName { get; } = "event";
}

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
    [JsonRequired, JsonPropertyName(A2AEventConverterViaKindDiscriminator<A2AEvent>.DiscriminatorPropertyName), JsonInclude, JsonPropertyOrder(int.MinValue)]
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

internal class A2AEventConverterViaKindDiscriminator<T> : JsonConverter<T> where T : A2AEvent
{
    internal const string DiscriminatorPropertyName = "kind";

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (!root.TryGetProperty(DiscriminatorPropertyName, out var kindProp) || kindProp.ValueKind is not JsonValueKind.String)
        {
            throw new A2AException($"Missing required '{DiscriminatorPropertyName}' discriminator for {typeof(T).Name}.", A2AErrorCode.InvalidRequest);
        }

        T? a2aEventObj = null;
        Exception? deserializationException = null;
        try
        {
            var kindValue = kindProp.Deserialize(A2AJsonUtilities.JsonContext.Default.A2AEventKind);
#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
            // We don't need to handle this because the previous Deserialize call would have thrown if the value was invalid.
            JsonTypeInfo typeInfo = kindValue switch
            {
                A2AEventKind.Message => options.GetTypeInfo(typeof(AgentMessage)),
                A2AEventKind.Task => options.GetTypeInfo(typeof(AgentTask)),
                A2AEventKind.StatusUpdate => options.GetTypeInfo(typeof(TaskStatusUpdateEvent)),
                A2AEventKind.ArtifactUpdate => options.GetTypeInfo(typeof(TaskArtifactUpdateEvent)),
            };
#pragma warning restore CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

            a2aEventObj = (T?)root.Deserialize(typeInfo);
        }
        catch (Exception e)
        {
            deserializationException = e;
        }

        if (deserializationException is not null || a2aEventObj is null)
        {
            throw new A2AException($"Failed to deserialize {kindProp.GetString()} event", deserializationException, A2AErrorCode.InvalidRequest);
        }

        return a2aEventObj;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var element = JsonSerializer.SerializeToElement(value, options.GetTypeInfo(value.GetType()));
        writer.WriteStartObject();

        foreach (var prop in element.EnumerateObject())
        {
            prop.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>
/// Base class for A2A events.
/// </summary>
[JsonConverter(typeof(A2AEventConverterViaKindDiscriminator<A2AEvent>))]
[JsonDerivedType(typeof(TaskStatusUpdateEvent))]
[JsonDerivedType(typeof(TaskArtifactUpdateEvent))]
[JsonDerivedType(typeof(AgentMessage))]
[JsonDerivedType(typeof(AgentTask))]
// You might be wondering why we don't use JsonPolymorphic here. The reason is that it automatically throws a NotSupportedException if the 
// discriminator isn't present or accounted for. In the case of A2A, we want to throw a more specific A2AException with an error code, so
// we implement our own converter to handle that, with the discriminator logic implemented by-hand.
public abstract class A2AEvent;

/// <summary>
/// A2A response objects.
/// </summary>
[JsonConverter(typeof(A2AEventConverterViaKindDiscriminator<A2AResponse>))]
[JsonDerivedType(typeof(AgentMessage))]
[JsonDerivedType(typeof(AgentTask))]
// You might be wondering why we don't use JsonPolymorphic here. The reason is that it automatically throws a NotSupportedException if the 
// discriminator isn't present or accounted for. In the case of A2A, we want to throw a more specific A2AException with an error code, so
// we implement our own converter to handle that, with the discriminator logic implemented by-hand.
public abstract class A2AResponse : A2AEvent;

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

        var kind = kindProp.GetString();
        JsonTypeInfo? typeInfo = kind switch
        {
            AgentMessage.DiscriminatorValue => options.GetTypeInfo(typeof(AgentMessage)),
            AgentTask.DiscriminatorValue => options.GetTypeInfo(typeof(AgentTask)),
            TaskStatusUpdateEvent.DiscriminatorValue => options.GetTypeInfo(typeof(TaskStatusUpdateEvent)),
            TaskArtifactUpdateEvent.DiscriminatorValue => options.GetTypeInfo(typeof(TaskArtifactUpdateEvent)),
            _ => null
        };

        T? evt = null;
        Exception? ex = null;
        try
        {
            evt = typeInfo is null ? null : (T?)root.Deserialize(typeInfo);
        }
        catch (Exception e)
        {
            ex = e;
        }

        if (ex is not null || evt is null)
        {
            throw new A2AException($"Failed to deserialize {kind} event", ex, A2AErrorCode.InvalidRequest);
        }

        return evt;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var (kind, typeInfo) = value switch
        {
            AgentMessage => (AgentMessage.DiscriminatorValue, options.GetTypeInfo(typeof(AgentMessage))!),
            AgentTask => (AgentTask.DiscriminatorValue, options.GetTypeInfo(typeof(AgentTask))!),
            TaskStatusUpdateEvent => (TaskStatusUpdateEvent.DiscriminatorValue, options.GetTypeInfo(typeof(TaskStatusUpdateEvent))!),
            TaskArtifactUpdateEvent => (TaskArtifactUpdateEvent.DiscriminatorValue, options.GetTypeInfo(typeof(TaskArtifactUpdateEvent))!),
            _ => throw new InvalidOperationException($"Unsupported A2AEvent type: {value.GetType().FullName}")
        };

        var element = JsonSerializer.SerializeToElement(value, typeInfo);
        writer.WriteStartObject();
        writer.WriteString(DiscriminatorPropertyName, kind);

        foreach (var prop in element.EnumerateObject())
        {
            prop.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}

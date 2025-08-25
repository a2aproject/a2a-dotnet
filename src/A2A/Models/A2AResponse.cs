using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Base class for A2A events.
/// </summary>
[JsonConverter(typeof(A2AEventConverter))]
public abstract class A2AEvent
{
    /// <summary>
    /// Event object discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    public abstract string Kind { get; }
}

/// <summary>
/// JSON converter for A2AEvent.
/// </summary>
sealed class A2AEventConverter : JsonConverter<A2AEvent>
{
    /// <inheritdoc/>
    public override A2AEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        if (!document.RootElement.TryGetProperty("kind", out var kindProperty) || kindProperty.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Missing or invalid 'kind' discriminator for A2AEvent.");
        }

        var kind = kindProperty.GetString();
        A2AEvent? result = kind switch
        {
            "message" => document.RootElement.Deserialize(A2AJsonUtilities.JsonContext.Default.Message),
            "task" => document.RootElement.Deserialize(A2AJsonUtilities.JsonContext.Default.AgentTask),
            "status-update" => document.RootElement.Deserialize(A2AJsonUtilities.JsonContext.Default.TaskStatusUpdateEvent),
            "artifact-update" => document.RootElement.Deserialize(A2AJsonUtilities.JsonContext.Default.TaskArtifactUpdateEvent),
            _ => null,
        };

        if (result is null)
        {
            throw new JsonException($"Unknown A2AEvent kind '{kind}'.");
        }

        return result;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, A2AEvent value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value)
        {
            case Message message:
                JsonSerializer.Serialize(writer, message, A2AJsonUtilities.JsonContext.Default.Message);
                break;
            case AgentTask task:
                JsonSerializer.Serialize(writer, task, A2AJsonUtilities.JsonContext.Default.AgentTask);
                break;
            case TaskStatusUpdateEvent taskStatusUpdateEvent:
                JsonSerializer.Serialize(writer, taskStatusUpdateEvent, A2AJsonUtilities.JsonContext.Default.TaskStatusUpdateEvent);
                break;
            case TaskArtifactUpdateEvent taskArtifactUpdateEvent:
                JsonSerializer.Serialize(writer, taskArtifactUpdateEvent, A2AJsonUtilities.JsonContext.Default.TaskArtifactUpdateEvent);
                break;
            default:
                throw new JsonException($"Unsupported A2AEvent runtime type {value.GetType().Name}");
        }
    }
}

/// <summary>
/// A2A response objects.
/// </summary>
[JsonConverter(typeof(A2AResponseConverter))]
public abstract class A2AResponse : A2AEvent;

/// <summary>
/// JSON converter for A2AResponse.
/// </summary>
sealed class A2AResponseConverter : JsonConverter<A2AResponse>
{
    private static readonly A2AEventConverter _eventConverter = new();

    /// <inheritdoc/>
    public override A2AResponse? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Delegate the deserialization to A2AEventConverter.
        var a2aEvent = _eventConverter.Read(ref reader, typeof(A2AEvent), options);
        if (a2aEvent is A2AResponse a2aResponse)
        {
            return a2aResponse;
        }
        throw new JsonException("JSON did not represent an A2AResponse instance.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, A2AResponse value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, A2AJsonUtilities.JsonContext.Default.A2AEvent);
    }
}

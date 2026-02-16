using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.V0_3;

internal abstract class BaseKindDiscriminatorConverter<TBase> : JsonConverter<TBase>
    where TBase : class
{
    internal const string DiscriminatorPropertyName = "kind";

    /// <summary>
    /// Gets the mapping from kind string values to their corresponding concrete types.
    /// </summary>
    protected abstract IReadOnlyDictionary<string, Type> KindToTypeMapping { get; }

    /// <summary>
    /// Gets the entity name used in error messages (e.g., "part", "file content", "event").
    /// </summary>
    protected abstract string DisplayName { get; }

    /// <summary>
    /// Reads an instance of <typeparamref name="TBase"/> from JSON using a kind discriminator.
    /// </summary>
    /// <param name="reader">The <see cref="Utf8JsonReader"/> to read from.</param>
    /// <param name="typeToConvert">The type to convert (ignored).</param>
    /// <param name="options">Serialization options used to obtain type metadata.</param>
    /// <returns>The deserialized instance of <typeparamref name="TBase"/>.</returns>
    public override TBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (!root.TryGetProperty(DiscriminatorPropertyName, out var kindProp))
        {
            throw new A2AException($"Missing required '{DiscriminatorPropertyName}' discriminator for {typeof(TBase).Name}.", A2AErrorCode.InvalidRequest);
        }

        if (kindProp.ValueKind is not JsonValueKind.String)
        {
            throw new A2AException($"Invalid '{DiscriminatorPropertyName}' discriminator for {typeof(TBase).Name}: '{(kindProp.ValueKind is JsonValueKind.Null ? "null" : kindProp)}'.", A2AErrorCode.InvalidRequest);
        }

        var kindValue = kindProp.GetString();
        if (string.IsNullOrEmpty(kindValue))
        {
            throw new A2AException($"Missing '{DiscriminatorPropertyName}' discriminator value for {typeof(TBase).Name}.", A2AErrorCode.InvalidRequest);
        }

        var kindToTypeMapping = KindToTypeMapping;
        if (!kindToTypeMapping.TryGetValue(kindValue!, out var targetType))
        {
            throw new A2AException($"Unknown {DisplayName} kind: '{kindValue}'", A2AErrorCode.InvalidRequest);
        }

        TBase? obj = null;
        Exception? deserializationException = null;
        try
        {
            var typeInfo = options.GetTypeInfo(targetType);
            obj = (TBase?)root.Deserialize(typeInfo);
        }
        catch (Exception e)
        {
            deserializationException = e;
        }

        if (deserializationException is not null || obj is null)
        {
            throw new A2AException($"Failed to deserialize '{kindValue}' {DisplayName}", deserializationException, A2AErrorCode.InvalidRequest);
        }

        return obj;
    }

    /// <summary>
    /// Writes the provided <typeparamref name="TBase"/> value to JSON.
    /// </summary>
    /// <param name="writer">The <see cref="Utf8JsonWriter"/> to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">Serialization options used to obtain type metadata.</param>
    public override void Write(Utf8JsonWriter writer, TBase value, JsonSerializerOptions options)
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

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>
/// Base class for JSON converters that use a "kind" discriminator to deserialize to different derived types.
/// </summary>
/// <typeparam name="TBase">The base type being converted.</typeparam>
/// <typeparam name="TKind">The enum type used as the discriminator.</typeparam>
internal abstract class BaseKindDiscriminatorConverter<TBase, TKind> : JsonConverter<TBase>
    where TBase : class
    where TKind : struct, Enum
{
    internal const string DiscriminatorPropertyName = "kind";

    /// <summary>
    /// Gets the mapping from kind enum values to their corresponding concrete types.
    /// </summary>
    protected abstract Type?[] TypeMapping { get; }

    /// <summary>
    /// Gets the entity name used in error messages (e.g., "part", "file content", "event").
    /// </summary>
    protected abstract string DisplayName { get; }

    /// <summary>
    /// Attempts to deserialize the kind enum value from the JSON property using the appropriate JsonTypeInfo.
    /// </summary>
    /// <param name="kindProp">The JSON property containing the kind value.</param>
    /// <param name="value">The deserialized enum value.</param>
    /// <returns>True if deserialization was successful; otherwise, false.</returns>
    protected abstract bool TryDeserializeKind(JsonElement kindProp, out TKind value);

    public override TBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (!root.TryGetProperty(DiscriminatorPropertyName, out var kindProp))
        {
            throw new A2AException($"Missing required '{DiscriminatorPropertyName}' discriminator for {typeof(TBase).Name}.", A2AErrorCode.InvalidRequest);
        }
        else if (kindProp.ValueKind is not JsonValueKind.String)
        {
            throw new A2AException($"Invalid '{DiscriminatorPropertyName}' discriminator for {typeof(TBase).Name}: '{(kindProp.ValueKind is JsonValueKind.Null ? "null" : kindProp)}'.", A2AErrorCode.InvalidRequest);
        }

        TBase? obj = null;
        Exception? deserializationException = null;
        try
        {
            if (!TryDeserializeKind(kindProp, out var kindValue))
            {
                throw new A2AException($"Unsupported {DisplayName} {DiscriminatorPropertyName}: '{kindProp.GetString()}'", A2AErrorCode.InvalidRequest);
            }

            var kindToTypeMapping = TypeMapping;
            var kindIndex = Convert.ToInt32(kindValue, CultureInfo.InvariantCulture);

            if (kindIndex < 0 || kindIndex >= kindToTypeMapping.Length || kindToTypeMapping[kindIndex] is not Type targetType)
            {
                throw new A2AException($"Unknown {DisplayName} kind: '{kindProp.GetString()}'", A2AErrorCode.InvalidRequest);
            }

            var typeInfo = options.GetTypeInfo(targetType);
            obj = (TBase?)root.Deserialize(typeInfo);
        }
        catch (Exception e)
        {
            deserializationException = e;
        }

        if (deserializationException is not null || obj is null)
        {
            throw new A2AException($"Failed to deserialize '{kindProp.GetString()}' {DisplayName}", deserializationException, A2AErrorCode.InvalidRequest);
        }

        return obj;
    }

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
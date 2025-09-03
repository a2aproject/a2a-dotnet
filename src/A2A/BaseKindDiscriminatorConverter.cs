using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

internal abstract class BaseKindDiscriminatorConverter<TBase, TKind> : JsonConverter<TBase>
    where TBase : class
    where TKind : struct, Enum
{
    internal const string DiscriminatorPropertyName = "kind";

    /// <summary>
    /// Gets the mapping from kind enum values to their corresponding concrete types.
    /// </summary>
    protected abstract DiscriminatorTypeMapping<TKind> TypeMapping { get; }

    /// <summary>
    /// Gets the JsonTypeInfo used to deserialize the discriminator value.
    /// </summary>
    protected abstract JsonTypeInfo<TKind> JsonTypeInfo { get; }

    /// <summary>
    /// Gets the sentinel <c>Unknown</c> value for the enum discriminator.
    /// </summary>
    protected abstract TKind UnknownValue { get; }

    /// <summary>
    /// Gets the entity name used in error messages (e.g., "part", "file content", "event").
    /// </summary>
    protected abstract string DisplayName { get; }

    /// <summary>
    /// Attempts to deserialize the kind enum value from the provided JsonElement using the configured JsonTypeInfo.
    /// </summary>
    /// <param name="kindProp">The JSON element containing the kind value.</param>
    /// <param name="value">When this method returns, contains the deserialized enum value if the operation succeeded; otherwise the UnknownValue.</param>
    /// <returns>True if deserialization succeeded; otherwise false.</returns>
    protected bool TryDeserializeKind(JsonElement kindProp, out TKind value)
    {
        value = this.UnknownValue;
        try
        {
            value = kindProp.Deserialize(this.JsonTypeInfo);
            return value.Equals(this.UnknownValue);
        }
        catch
        {
            return false;
        }
    }

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

            if (kindIndex < 0 || kindIndex >= kindToTypeMapping.Count || kindToTypeMapping[kindIndex] is not Type targetType)
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

/// <summary>
/// Initializes a new instance of the <see cref="DiscriminatorTypeMapping{TEnum}"/> class.
/// </summary>
/// <param name="typeMappings">The concrete types corresponding to the enum values (excluding the leading unknown slot).</param>
internal class DiscriminatorTypeMapping<TEnum>(params Type[] typeMappings) : IReadOnlyList<Type?> where TEnum : Enum
{
    private readonly IReadOnlyList<Type?> _typeMappings = [
            null, // index 0 reserved for Unknown/null
            .. typeMappings
        ];

    /// <summary>
    /// Gets the number of mappings provided (including the leading null slot).
    /// </summary>
    public int Count => _typeMappings.Count;

    /// <summary>
    /// Gets an enumerator over the mapped types (including the leading null entry).
    /// </summary>
    public IEnumerator<Type?> GetEnumerator() => _typeMappings.GetEnumerator();

    /// <summary>
    /// Gets the mapped <see cref="Type"/> at the specified index.
    /// </summary>
    /// <param name="index">The index to retrieve.</param>
    public Type? this[int index] => _typeMappings[index];

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

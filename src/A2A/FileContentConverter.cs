using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Custom JSON converter for FileContent that determines the concrete type
/// based on the presence of "bytes" or "uri" properties rather than a discriminator.
/// This aligns with the A2A spec which doesn't define a "kind" property for FileContent.
/// </summary>
internal class FileContentConverter : JsonConverter<FileContent>
{
    /// <summary>
    /// Reads a FileContent from JSON by detecting whether it contains "bytes" or "uri" property.
    /// </summary>
    /// <param name="reader">The reader to read from.</param>
    /// <param name="typeToConvert">The type to convert.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The deserialized FileContent.</returns>
    public override FileContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        // Determine type based on presence of required properties
        bool hasBytes = root.TryGetProperty("bytes", out var bytesProperty) &&
                       bytesProperty.ValueKind == JsonValueKind.String;
        bool hasUri = root.TryGetProperty("uri", out var uriProperty) &&
                     uriProperty.ValueKind == JsonValueKind.String;

        Type targetType;
        if (hasBytes && !hasUri)
        {
            targetType = typeof(FileWithBytes);
        }
        else if (hasUri && !hasBytes)
        {
            targetType = typeof(FileWithUri);
        }
        else if (hasBytes && hasUri)
        {
            throw new A2AException("FileContent cannot have both 'bytes' and 'uri' properties", A2AErrorCode.InvalidRequest);
        }
        else
        {
            throw new A2AException("FileContent must have either 'bytes' or 'uri' property", A2AErrorCode.InvalidRequest);
        }

        FileContent? obj = null;
        Exception? deserializationException = null;
        try
        {
            var typeInfo = options.GetTypeInfo(targetType);
            obj = (FileContent?)root.Deserialize(typeInfo);
        }
        catch (Exception e)
        {
            deserializationException = e;
        }

        if (deserializationException is not null || obj is null)
        {
            var typeName = targetType == typeof(FileWithBytes) ? "FileWithBytes" : "FileWithUri";
            throw new A2AException($"Failed to deserialize {typeName}", deserializationException, A2AErrorCode.InvalidRequest);
        }

        return obj;
    }

    /// <summary>
    /// Writes a FileContent to JSON using the appropriate derived type serializer.
    /// </summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, FileContent value, JsonSerializerOptions options)
    {
        // Get safe options that don't include this converter to avoid infinite recursion
        var safeOptions = GetSafeOptions(options);

        // Serialize using the actual type
        var typeInfo = safeOptions.GetTypeInfo(value.GetType());
        var element = JsonSerializer.SerializeToElement(value, typeInfo);

        writer.WriteStartObject();
        foreach (var prop in element.EnumerateObject())
        {
            // Skip the "kind" property if it exists
            if (prop.Name == "kind")
                continue;

            prop.WriteTo(writer);
        }
        writer.WriteEndObject();
    }

    private static JsonSerializerOptions? _safeOptions;

    /// <summary>
    /// Gets serializer options with this converter removed to avoid infinite recursion.
    /// </summary>
    /// <param name="options">The original serializer options.</param>
    /// <returns>Safe serializer options without this converter.</returns>
    private static JsonSerializerOptions GetSafeOptions(JsonSerializerOptions options)
    {
        if (_safeOptions is null)
        {
            var safeOptions = new JsonSerializerOptions(options);

            // Remove this converter to avoid infinite recursion
            for (int i = safeOptions.Converters.Count - 1; i >= 0; i--)
            {
                if (safeOptions.Converters[i] is FileContentConverter)
                {
                    safeOptions.Converters.RemoveAt(i);
                    break;
                }
            }

            _safeOptions = safeOptions;
        }

        return _safeOptions;
    }
}
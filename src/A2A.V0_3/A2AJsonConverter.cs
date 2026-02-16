using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace A2A.V0_3
{
    internal interface IA2AJsonConverter;

    /// <summary>
    /// Provides a base JSON converter for types in the A2A protocol, enabling custom error handling and
    /// safe delegation to source-generated or built-in converters for the target type.
    /// </summary>
    /// <typeparam name="T">The type to convert.</typeparam>
    internal class A2AJsonConverter<T> : JsonConverter<T>, IA2AJsonConverter where T : notnull
    {
        private static JsonSerializerOptions? _serializerOptionsWithoutThisConverter;
        private static JsonSerializerOptions? _outsideSerializerOptions;

        /// <summary>
        /// Reads and converts the JSON to type <typeparamref name="T"/>.
        /// </summary>
        /// <param name="reader">The reader to read from.</param>
        /// <param name="typeToConvert">The type to convert.</param>
        /// <param name="options">The serializer options to use.</param>
        /// <returns>The deserialized value of type <typeparamref name="T"/>.</returns>
        /// <exception cref="A2AException">
        /// Thrown when deserialization fails, wrapping the original exception and providing an <see cref="A2AErrorCode.InvalidRequest"/> error code.
        /// </exception>
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var d = JsonDocument.ParseValue(ref reader);
        	return DeserializeImpl(typeToConvert, GetSafeOptions(options), d);
        }

        /// <summary>
        /// Deserializes the specified <see cref="JsonDocument"/> to type <typeparamref name="T"/> using the provided options.
        /// </summary>
        /// <param name="typeToConvert">The type to convert.</param>
        /// <param name="options">The serializer options to use.</param>
        /// <param name="document">The JSON document to deserialize.</param>
        /// <returns>The deserialized value of type <typeparamref name="T"/>.</returns>
        protected virtual T? DeserializeImpl(Type typeToConvert, JsonSerializerOptions options, JsonDocument document) => document.Deserialize((JsonTypeInfo<T>)options.GetTypeInfo(typeToConvert));

        /// <summary>
        /// Writes the specified value as JSON.
        /// </summary>
        /// <param name="writer">The writer to write to.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="options">The serializer options to use.</param>
        /// <exception cref="A2AException">
        /// Thrown when serialization fails, wrapping the original exception and providing an <see cref="A2AErrorCode.InternalError"/> error code.
        /// </exception>
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            try
            {
                if (value is null)
                {
                    writer.WriteNullValue();
                    return;
                }

                SerializeImpl(writer, value, GetSafeOptions(options));
            }
            catch (Exception e)
            {
                throw new A2AException($"Failed to serialize {typeof(T).Name}: {e.Message}", e, A2AErrorCode.InternalError);
            }
        }

        /// <summary>
        /// Serializes the specified value to JSON using the provided options.
        /// </summary>
        /// <param name="writer">The writer to write to.</param>
        /// <param name="value">The value to serialize.</param>
        /// <param name="options">The serializer options to use.</param>
        protected virtual void SerializeImpl(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, value, (JsonTypeInfo<T>)options.GetTypeInfo(value.GetType()));

        /// <summary>
        /// Returns a copy of the provided <see cref="JsonSerializerOptions"/> with this
        /// <see cref="A2AJsonConverter{T}"/> removed from its <see cref="JsonSerializerOptions.Converters"/> chain.
        /// </summary>
        /// <remarks>
        /// This converter delegates to the source-generated or built-in converter for <typeparamref name="T"/> by
        /// resolving <see cref="JsonTypeInfo"/> from the options. If the original options are used as-is, this converter
        /// would be selected again, causing infinite recursion and a stack overflow. Cloning the options and removing
        /// this converter ensures the underlying, "real" converter handles serialization/deserialization of <typeparamref name="T"/>.
        ///
        /// The returned options are cached per closed generic type to avoid repeated allocations. The cache assumes a
        /// stable options instance; if multiple distinct options are used, the first encountered configuration is captured.
        /// </remarks>
        /// <param name="options">Caller options; used as a template for the safe copy.</param>
        /// <returns>A copy of the options that can safely resolve the underlying converter for <typeparamref name="T"/>.</returns>
        private static JsonSerializerOptions GetSafeOptions(JsonSerializerOptions options)
        {
            if (_serializerOptionsWithoutThisConverter is null)
            {
                // keep reeference to original options for cache validation 
                _outsideSerializerOptions = options;

                // Clone options so we can modify the converters chain safely
                var baseOptions = new JsonSerializerOptions(options);

                // Remove this converter so base/source-generated converter handles T, otherwise stack overflow
                for (int i = baseOptions.Converters.Count - 1; i >= 0; i--)
                {
                    if (baseOptions.Converters[i] is A2AJsonConverter<T>)
                    {
                        baseOptions.Converters.RemoveAt(i);
                        break;
                    }
                }

                _serializerOptionsWithoutThisConverter = baseOptions;
            }
            else if (_outsideSerializerOptions != options && options != _serializerOptionsWithoutThisConverter)
            {
                // Unexpected!!! This caching is based on promise that A2A will use only ONE instance of SerializerOptions
                // and we can therefore cache modified SerializerOptions without dealing with invalidation and pairing.
                // Since this is possible only by internal code, some recent code changes must have had broke this promise
                Debug.Fail("This should never happen");
                throw new InvalidOperationException();
            }

            return _serializerOptionsWithoutThisConverter;
        }
    }
}
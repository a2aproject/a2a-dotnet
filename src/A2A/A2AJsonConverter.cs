using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace A2A
{
    internal class A2AJsonConverter<T> : JsonConverter<T>
    {
        private static JsonSerializerOptions? _serializerOptionsWithoutThisConverter;

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

            return _serializerOptionsWithoutThisConverter;
        }

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var d = JsonDocument.ParseValue(ref reader);
            var baseOptions = GetSafeOptions(options);
            return d.Deserialize((JsonTypeInfo<T>)baseOptions.GetTypeInfo(typeToConvert));
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            try
            {
                if (value is null)
                {
                    writer.WriteNullValue();
                    return;
                }

                var baseOptions = GetSafeOptions(options);
                JsonSerializer.Serialize(writer, value, (JsonTypeInfo<T>)baseOptions.GetTypeInfo(value.GetType()));
            }
            catch (Exception e)
            {
                throw new A2AException($"Failed to serialize {typeof(T).Name}: {e.Message}", e, A2AErrorCode.InternalError);
            }
        }
    }
}
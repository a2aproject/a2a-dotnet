using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace A2A
{
    internal class A2AJsonConverter<T> : JsonConverter<T>
    {
        private static JsonSerializerOptions CreateBaseOptions(JsonSerializerOptions options)
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

            return baseOptions;
        }

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var d = JsonDocument.ParseValue(ref reader);
            try
            {
                var baseOptions = CreateBaseOptions(options);
                return d.Deserialize((JsonTypeInfo<T>)baseOptions.GetTypeInfo(typeToConvert));
            }
            catch (Exception e)
            {
                throw new A2AException($"Failed to deserialize {typeof(T).Name}: {e.Message}", e, A2AErrorCode.InvalidRequest);
            }
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

                var baseOptions = CreateBaseOptions(options);
                JsonSerializer.Serialize(writer, value, (JsonTypeInfo<T>)baseOptions.GetTypeInfo(value.GetType()));
            }
            catch (Exception e)
            {
                throw new A2AException($"Failed to serialize {typeof(T).Name}: {e.Message}", e, A2AErrorCode.InternalError);
            }
        }
    }
}
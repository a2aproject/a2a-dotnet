namespace A2A.Grpc;

using System.Buffers;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

/// <summary>
/// Bidirectional conversions between the A2A domain model (System.Text.Json shaped) and the
/// protobuf-generated types under <c>A2A.Grpc.Protos</c>.
/// </summary>
/// <remarks>
/// This partial holds the low-level primitives — <see cref="Value"/>/<see cref="Struct"/> and
/// metadata dictionaries — that the message and agent-card mappings build on. All conversions are
/// reflection-free so the assembly stays Native AOT compatible.
/// </remarks>
internal static partial class ProtoMap
{
    // ---- Scalars ------------------------------------------------------------------------------

    // Returns null for an empty protobuf string (protobuf scalar strings are never null).
    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    // ---- Metadata (Dictionary&lt;string, JsonElement&gt; &lt;-&gt; google.protobuf.Struct) ------------------

    // Converts a domain metadata dictionary to a protobuf Struct, or null if absent.
    public static Struct? ToProtoStruct(IReadOnlyDictionary<string, JsonElement>? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        var result = new Struct();
        foreach (var pair in metadata)
        {
            result.Fields[pair.Key] = ToProtoValue(pair.Value);
        }

        return result;
    }

    // Converts a protobuf Struct to a domain metadata dictionary, or null if empty.
    public static Dictionary<string, JsonElement>? ToMetadata(Struct? source)
    {
        if (source is null || source.Fields.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, JsonElement>(source.Fields.Count);
        foreach (var pair in source.Fields)
        {
            result[pair.Key] = ToJsonElement(pair.Value);
        }

        return result;
    }

    // ---- JSON value (JsonElement &lt;-&gt; google.protobuf.Value) ------------------------------------

    // Converts a JsonElement to a protobuf Value.
    public static Value ToProtoValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var structValue = new Struct();
                foreach (var property in element.EnumerateObject())
                {
                    structValue.Fields[property.Name] = ToProtoValue(property.Value);
                }

                return Value.ForStruct(structValue);

            case JsonValueKind.Array:
                var list = new List<Value>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(ToProtoValue(item));
                }

                return Value.ForList([.. list]);

            case JsonValueKind.String:
                return Value.ForString(element.GetString()!);

            case JsonValueKind.Number:
                return Value.ForNumber(element.GetDouble());

            case JsonValueKind.True:
                return Value.ForBool(true);

            case JsonValueKind.False:
                return Value.ForBool(false);

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return Value.ForNull();
        }
    }

    // Converts a protobuf Value to a JsonElement.
    public static JsonElement ToJsonElement(Value value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteValue(writer, value);
        }

        var reader = new Utf8JsonReader(buffer.WrittenSpan);
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.Clone();
    }

    private static void WriteValue(Utf8JsonWriter writer, Value value)
    {
        switch (value.KindCase)
        {
            case Value.KindOneofCase.NumberValue:
                writer.WriteNumberValue(value.NumberValue);
                break;

            case Value.KindOneofCase.StringValue:
                writer.WriteStringValue(value.StringValue);
                break;

            case Value.KindOneofCase.BoolValue:
                writer.WriteBooleanValue(value.BoolValue);
                break;

            case Value.KindOneofCase.StructValue:
                writer.WriteStartObject();
                foreach (var field in value.StructValue.Fields)
                {
                    writer.WritePropertyName(field.Key);
                    WriteValue(writer, field.Value);
                }

                writer.WriteEndObject();
                break;

            case Value.KindOneofCase.ListValue:
                writer.WriteStartArray();
                foreach (var item in value.ListValue.Values)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;

            case Value.KindOneofCase.NullValue:
            case Value.KindOneofCase.None:
            default:
                writer.WriteNullValue();
                break;
        }
    }
}

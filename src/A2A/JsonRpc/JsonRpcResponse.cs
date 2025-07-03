using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public JsonNode? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }

    public static JsonRpcResponse CreateJsonRpcResponse<T>(string requestId, T result, JsonTypeInfo? resultTypeInfo = null)
    {
        resultTypeInfo ??= (JsonTypeInfo<T>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

        return new JsonRpcResponse()
        {
            Id = requestId,
            Result = result is not null ? JsonSerializer.SerializeToNode(result, resultTypeInfo) : null
        };
    }

    public static JsonRpcResponse InvalidParamsResponse(string requestId) => new()
    {
        Id = requestId,
        Error = new JsonRpcError()
        {
            Code = -32602,
            Message = "Invalid parameters",
        },
    };

    public static JsonRpcResponse MethodNotFoundResponse(string requestId) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32601,
            Message = "Method not found"
        },
    };

    public static JsonRpcResponse InternalErrorResponse(string requestId, string message) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32603,
            Message = message
        },
    };

    public static JsonRpcResponse ParseErrorResponse(string requestId, string? message) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32700,
            Message = message ?? "Invalid JSON payload",
        },
    };
}

// public class JsonRpcResponse<T> : JsonRpcResponse
// {
//     public static JsonRpcResponse<T> CreateJsonRpcResponse(string requestId, T result)
//     {
//         return new JsonRpcResponse<T>()
//         {
//             Id = requestId,
//             Result = result,
//             JsonRpc = "2.0"
//         };
//     }

//     [JsonPropertyName("result")]
//     public T? Result { get; set; }

// }

// public class JsonRpcResponseConverter<T> : JsonConverter<JsonRpcResponse<T>>
// {
//     public override JsonRpcResponse<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
//     {
//         using (JsonDocument document = JsonDocument.ParseValue(ref reader))
//         {
//             var rootElement = document.RootElement;

//             var jsonRpc = rootElement.GetProperty("jsonrpc").GetString();
//             var id = rootElement.GetProperty("id").GetString();

//             JsonRpcResponse<T> response = new JsonRpcResponse<T>
//             {
//                 JsonRpc = jsonRpc ?? "2.0",
//                 Id = id ?? string.Empty
//             };

//             if (rootElement.TryGetProperty("result", out var resultProperty))
//             {
//                 response.Result = resultProperty.Deserialize<T>(options);
//             }

//             return response;
//         }
//     }

//     public override void Write(Utf8JsonWriter writer, JsonRpcResponse<T> value, JsonSerializerOptions options)
//     {
//         writer.WriteStartObject();

//         writer.WriteString("jsonrpc", value.JsonRpc);
//         writer.WriteString("id", value.Id);

//         if (value.Result != null)
//         {
//             writer.WritePropertyName("result");
//             JsonSerializer.Serialize(writer, value.Result, value.Result.GetType(), options);
//         }

//         writer.WriteEndObject();
//     }
// }

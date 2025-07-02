using System.Text.Json;

namespace A2A;

public static class JsonRpcErrorResponses
{
    public static JsonRpcErrorResponse InvalidParamsResponse(string requestId) => new()
    {
        Id = requestId,
        Error = new InvalidParamsError(),
        JsonRpc = "2.0"
    };

    public static JsonRpcErrorResponse MethodNotFoundResponse(string requestId) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32601,
            Message = "Method not found"
        },
        JsonRpc = "2.0"
    };

    public static JsonRpcErrorResponse InternalErrorResponse(string requestId, string message) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32603,
            Message = message
        },
        JsonRpc = "2.0"
    };

    public static JsonRpcErrorResponse ParseErrorResponse(string requestId, string message) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32700,
            Message = message
        },
        JsonRpc = "2.0"
    };
}

public class JsonRpcError
{
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public JsonElement? Data { get; set; }

    // Deserialize a JsonRpcError from a JsonElement
    public static JsonRpcError FromJson(JsonElement jsonElement)
    {
        return JsonSerializer.Deserialize<JsonRpcError>(jsonElement.GetRawText(), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }) ?? throw new InvalidOperationException("Failed to deserialize JsonRpcError.");
    }

    // Serialize a JsonRpcError to JSON
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
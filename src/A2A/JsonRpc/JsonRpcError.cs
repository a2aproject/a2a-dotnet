using System.Text.Json;

namespace A2A;

public static class JsonRpcErrorResponses
{
    public static JsonRpcErrorResponse InvalidParamsResponse(string requestId) => new()
    {
        Id = requestId,
        Error = new JsonRpcError()
        {
            Code = -32602,
            Message = "Invalid parameters",
        },
    };

    public static JsonRpcErrorResponse MethodNotFoundResponse(string requestId) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32601,
            Message = "Method not found"
        },
    };

    public static JsonRpcErrorResponse InternalErrorResponse(string requestId, string message) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32603,
            Message = message
        },
    };

    public static JsonRpcErrorResponse ParseErrorResponse(string requestId, string? message) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32700,
            Message = message ?? "Invalid JSON payload",
        },
    };
}

public class JsonRpcError
{
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public JsonElement? Data { get; set; }

    // Deserialize a JsonRpcError from a JsonElement
    public static JsonRpcError FromJson(JsonElement jsonElement) =>
        jsonElement.Deserialize(A2AJsonUtilities.JsonContext.Default.JsonRpcError) ??
        throw new InvalidOperationException("Failed to deserialize JsonRpcError.");

    // Serialize a JsonRpcError to JSON
    public string ToJson() => JsonSerializer.Serialize(this, A2AJsonUtilities.JsonContext.Default.JsonRpcError);
}
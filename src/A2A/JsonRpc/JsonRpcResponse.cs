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

    public static JsonRpcResponse TaskNotFoundResponse(string requestId) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32001,
            Message = "Task not found",
        },
    };

    public static JsonRpcResponse TaskNotCancelableResponse(string requestId) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32002,
            Message = "Task cannot be canceled",
        },
    };

    public static JsonRpcResponse MethodNotFoundResponse(string requestId) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32601,
            Message = "Method not found",
        },
    };

    public static JsonRpcResponse PushNotificationNotSupportedResponse(string requestId) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32003,
            Message = "Push notification not supported",
        },
    };

    public static JsonRpcResponse InternalErrorResponse(string requestId, string? message = null) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32603,
            Message = message ?? "Internal error",
        },
    };

    public static JsonRpcResponse ParseErrorResponse(string requestId, string? message = null) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32700,
            Message = message ?? "Invalid JSON payload",
        },
    };

    public static JsonRpcResponse UnsupportedOperationResponse(string requestId, string? message = null) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32004,
            Message = message ?? "Unsupported operation",
        },
    };

    public static JsonRpcResponse ContentTypeNotSupportedResponse(string requestId, string? message = null) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32005,
            Message = message ?? "Content type not supported",
        },
    };
}
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>
/// Represents a JSON-RPC response message.
/// </summary>
public class JsonRpcResponse
{
    /// <summary>
    /// Gets or sets the JSON-RPC version. Must be "2.0".
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    /// <summary>
    /// Gets or sets the request identifier, which correlates the response with its request.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the result of the method invocation. This property is present on success.
    /// </summary>
    [JsonPropertyName("result")]
    public JsonNode? Result { get; set; }

    /// <summary>
    /// Gets or sets the error information. This property is present when an error occurred.
    /// </summary>
    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }

    /// <summary>
    /// Creates a successful JSON-RPC response with the specified result.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="requestId">The ID of the request being responded to.</param>
    /// <param name="result">The result to include in the response.</param>
    /// <param name="resultTypeInfo">Optional JSON type information for serialization.</param>
    /// <returns>A new <see cref="JsonRpcResponse"/> containing the result.</returns>
    public static JsonRpcResponse CreateJsonRpcResponse<T>(string requestId, T result, JsonTypeInfo? resultTypeInfo = null)
    {
        resultTypeInfo ??= (JsonTypeInfo<T>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

        return new JsonRpcResponse()
        {
            Id = requestId,
            Result = result is not null ? JsonSerializer.SerializeToNode(result, resultTypeInfo) : null
        };
    }

    /// <summary>
    /// Creates a JSON-RPC error response for invalid parameters.
    /// </summary>
    /// <param name="requestId">The ID of the request being responded to.</param>
    /// <returns>A new <see cref="JsonRpcResponse"/> with an invalid parameters error.</returns>
    public static JsonRpcResponse InvalidParamsResponse(string requestId) => new()
    {
        Id = requestId,
        Error = new JsonRpcError()
        {
            Code = -32602,
            Message = "Invalid parameters",
        },
    };

    /// <summary>
    /// Creates a JSON-RPC error response for when a task is not found.
    /// </summary>
    /// <param name="requestId">The ID of the request being responded to.</param>
    /// <returns>A new <see cref="JsonRpcResponse"/> with a task not found error.</returns>
    public static JsonRpcResponse TaskNotFoundResponse(string requestId) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32001,
            Message = "Task not found",
        },
    };

    /// <summary>
    /// Creates a JSON-RPC error response for when a task cannot be cancelled.
    /// </summary>
    /// <param name="requestId">The ID of the request being responded to.</param>
    /// <returns>A new <see cref="JsonRpcResponse"/> with a task not cancelable error.</returns>
    public static JsonRpcResponse TaskNotCancelableResponse(string requestId) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32002,
            Message = "Task cannot be canceled",
        },
    };

    /// <summary>
    /// Creates a JSON-RPC error response for when a method is not found.
    /// </summary>
    /// <param name="requestId">The ID of the request being responded to.</param>
    /// <returns>A new <see cref="JsonRpcResponse"/> with a method not found error.</returns>
    public static JsonRpcResponse MethodNotFoundResponse(string requestId) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32601,
            Message = "Method not found",
        },
    };

    /// <summary>
    /// Creates a JSON-RPC error response for when push notifications are not supported.
    /// </summary>
    /// <param name="requestId">The ID of the request being responded to.</param>
    /// <returns>A new <see cref="JsonRpcResponse"/> with a push notification not supported error.</returns>
    public static JsonRpcResponse PushNotificationNotSupportedResponse(string requestId) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32003,
            Message = "Push notification not supported",
        },
    };

    /// <summary>
    /// Creates a JSON-RPC error response for internal errors.
    /// </summary>
    /// <param name="requestId">The ID of the request being responded to.</param>
    /// <param name="message">An optional custom error message.</param>
    /// <returns>A new <see cref="JsonRpcResponse"/> with an internal error.</returns>
    public static JsonRpcResponse InternalErrorResponse(string requestId, string? message = null) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32603,
            Message = message ?? "Internal error",
        },
    };

    /// <summary>
    /// Creates a JSON-RPC error response for parse errors.
    /// </summary>
    /// <param name="requestId">The ID of the request being responded to.</param>
    /// <param name="message">An optional custom error message.</param>
    /// <returns>A new <see cref="JsonRpcResponse"/> with a parse error.</returns>
    public static JsonRpcResponse ParseErrorResponse(string requestId, string? message = null) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32700,
            Message = message ?? "Invalid JSON payload",
        },
    };

    /// <summary>
    /// Creates a JSON-RPC error response for unsupported operations.
    /// </summary>
    /// <param name="requestId">The ID of the request being responded to.</param>
    /// <param name="message">An optional custom error message.</param>
    /// <returns>A new <see cref="JsonRpcResponse"/> with an unsupported operation error.</returns>
    public static JsonRpcResponse UnsupportedOperationResponse(string requestId, string? message = null) => new()
    {
        Id = requestId,
        Error = new JsonRpcError
        {
            Code = -32004,
            Message = message ?? "Unsupported operation",
        },
    };

    /// <summary>
    /// Creates a JSON-RPC error response for unsupported content types.
    /// </summary>
    /// <param name="requestId">The ID of the request being responded to.</param>
    /// <param name="message">An optional custom error message.</param>
    /// <returns>A new <see cref="JsonRpcResponse"/> with a content type not supported error.</returns>
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
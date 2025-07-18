using A2A.AspNetCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Custom JsonConverter for JsonRpcRequest that validates fields during deserialization.
/// </summary>
internal sealed class JsonRpcRequestConverter : JsonConverter<JsonRpcRequest>
{
    /// <summary>
    /// The supported JSON-RPC version.
    /// </summary>
    private const string JsonRpcSupportedVersion = "2.0";

    public override JsonRpcRequest? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? requestId = null;

        try
        {
            // Create JsonElement from Utf8JsonReader
            using var jsonDoc = JsonDocument.ParseValue(ref reader);
            var rootElement = jsonDoc.RootElement;

            // Validate the JSON-RPC request structure
            requestId = ReadAndValidateIdField(rootElement);

            return new JsonRpcRequest
            {
                Id = requestId,
                JsonRpc = ReadAndValidateJsonRpcField(rootElement, requestId),
                Method = ReadAndValidateMethodField(rootElement, requestId),
                Params = ReadAndValidateParamsField(rootElement, requestId)
            };
        }
        catch (JsonException ex)
        {
            throw new A2AException("Invalid JSON-RPC request payload.", ex, A2AErrorCode.ParseError);
        }
    }

    public override void Write(Utf8JsonWriter writer, JsonRpcRequest value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value), "Cannot serialize a null JsonRpcRequest.");
        }

        writer.WriteStartObject();
        writer.WriteString("jsonrpc", value.JsonRpc);
        writer.WriteString("id", value.Id);
        writer.WriteString("method", value.Method);

        if (value.Params.HasValue)
        {
            writer.WritePropertyName("params");
            value.Params.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Reads and validates the 'id' field of a JSON-RPC request.
    /// </summary>
    /// <param name="rootElement">The root JSON element containing the request.</param>
    /// <returns>The extracted request ID as a string, or null if not present.</returns>
    private static string? ReadAndValidateIdField(JsonElement rootElement)
    {
        if (rootElement.TryGetProperty("id", out var idElement))
        {
            if (idElement.ValueKind != JsonValueKind.String &&
                idElement.ValueKind != JsonValueKind.Number &&
                idElement.ValueKind != JsonValueKind.Null)
            {
                throw new A2AException("Invalid JSON-RPC request: 'id' field must be a string, number, or null.", A2AErrorCode.InvalidRequest);
            }

            return idElement.ValueKind == JsonValueKind.Null ? null : idElement.ToString();
        }

        return null;
    }

    /// <summary>
    /// Reads and validates the 'jsonrpc' field of a JSON-RPC request.
    /// </summary>
    /// <param name="rootElement">The root JSON element containing the request.</param>
    /// <param name="requestId">The request ID for error context.</param>
    /// <returns>The JSON-RPC version as a string.</returns>
    private static string ReadAndValidateJsonRpcField(JsonElement rootElement, string? requestId)
    {
        if (rootElement.TryGetProperty("jsonrpc", out var jsonRpcElement))
        {
            var jsonRpc = jsonRpcElement.GetString();

            if (jsonRpc != JsonRpcSupportedVersion)
            {
                throw new A2AException("Invalid JSON-RPC request: 'jsonrpc' field must be '2.0'.", A2AErrorCode.InvalidRequest)
                    .WithRequestId(requestId);
            }

            return jsonRpc;
        }

        throw new A2AException("Invalid JSON-RPC request: missing 'jsonrpc' field.", A2AErrorCode.InvalidRequest)
            .WithRequestId(requestId);
    }

    /// <summary>
    /// Reads and validates the 'method' field of a JSON-RPC request.
    /// </summary>
    /// <param name="rootElement">The root JSON element containing the request.</param>
    /// <param name="requestId">The request ID for error context.</param>
    /// <returns>The method name as a string.</returns>
    private static string ReadAndValidateMethodField(JsonElement rootElement, string? requestId)
    {
        if (rootElement.TryGetProperty("method", out var methodElement))
        {
            var method = methodElement.GetString();
            if (string.IsNullOrEmpty(method))
            {
                throw new A2AException("Invalid JSON-RPC request: missing 'method' field.", A2AErrorCode.InvalidRequest)
                    .WithRequestId(requestId);
            }

            if (!A2AMethods.IsValidMethod(method!))
            {
                throw new A2AException("Invalid JSON-RPC request: 'method' field is not a valid A2A method.", A2AErrorCode.MethodNotFound)
                    .WithRequestId(requestId);
            }

            return method!;
        }

        throw new A2AException("Invalid JSON-RPC request: missing 'method' field.", A2AErrorCode.InvalidRequest)
            .WithRequestId(requestId);
    }

    /// <summary>
    /// Reads and validates the 'params' field of a JSON-RPC request.
    /// </summary>
    /// <param name="rootElement">The root JSON element containing the request.</param>
    /// <param name="requestId">The request ID for error context.</param>
    /// <returns>The 'params' element if it exists and is valid.</returns>
    private static JsonElement? ReadAndValidateParamsField(JsonElement rootElement, string? requestId)
    {
        if (rootElement.TryGetProperty("params", out var paramsElement))
        {
            if (paramsElement.ValueKind != JsonValueKind.Object &&
                paramsElement.ValueKind != JsonValueKind.Undefined &&
                paramsElement.ValueKind != JsonValueKind.Null)
            {
                throw new A2AException("Invalid JSON-RPC request: 'params' field must be an object or null.", A2AErrorCode.InvalidParams)
                    .WithRequestId(requestId);
            }
        }

        return paramsElement.ValueKind == JsonValueKind.Null || paramsElement.ValueKind == JsonValueKind.Undefined
            ? null
            : paramsElement.Clone();
    }
}
using System.Text.Json;

namespace A2A;

/// <summary>
/// Represents an error in a JSON-RPC 2.0 response.
/// </summary>
public class JsonRpcError
{
    /// <summary>
    /// Gets or sets the error code indicating the type of error that occurred.
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// Gets or sets a short description of the error.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets additional information about the error, if any.
    /// </summary>
    public JsonElement? Data { get; set; }

    /// <summary>
    /// Deserializes a JsonRpcError from a JsonElement.
    /// </summary>
    /// <param name="jsonElement">The JsonElement containing the error data.</param>
    /// <returns>A new <see cref="JsonRpcError"/> instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when deserialization fails.</exception>
    public static JsonRpcError FromJson(JsonElement jsonElement) =>
        jsonElement.Deserialize(A2AJsonUtilities.JsonContext.Default.JsonRpcError) ??
        throw new InvalidOperationException("Failed to deserialize JsonRpcError.");

    /// <summary>
    /// Serializes the JsonRpcError to a JSON string.
    /// </summary>
    /// <returns>A JSON string representation of the error.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, A2AJsonUtilities.JsonContext.Default.JsonRpcError);
}
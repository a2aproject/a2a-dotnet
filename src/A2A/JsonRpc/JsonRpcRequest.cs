using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a JSON-RPC 2.0 Request object.
/// </summary>
public class JsonRpcRequest
{
    /// <summary>
    /// Gets or sets the version of the JSON-RPC protocol.
    /// </summary>
    /// <remarks>
    /// MUST be exactly "2.0"
    /// </remarks>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    /// <summary>
    /// Gets or sets the identifier established by the Client that MUST contain a String, Number.
    /// </summary>
    /// <remarks>
    /// Numbers SHOULD NOT contain fractional parts.
    /// </remarks>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the string containing the name of the method to be invoked.
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the structured value that holds the parameter values to be used during the invocation of the method.
    /// </summary>
    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

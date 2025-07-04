using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents a JSON-RPC request message.
/// </summary>
public class JsonRpcRequest
{
    /// <summary>
    /// Gets or sets the JSON-RPC version. Must be "2.0".
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    /// <summary>
    /// Gets or sets the request identifier, which can be used to correlate the request with its response.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the method to be invoked.
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the parameters to be passed to the method, if any.
    /// </summary>
    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

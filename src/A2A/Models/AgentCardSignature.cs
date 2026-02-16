using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// AgentCardSignature represents a JWS signature of an AgentCard.
/// This follows the JSON format of an RFC 7515 JSON Web Signature (JWS).
/// </summary>
public sealed class AgentCardSignature
{
    /// <summary>
    /// The protected JWS header for the signature. This is a Base64url-encoded
    /// JSON object, as per RFC 7515.
    /// </summary>
    [JsonPropertyName("protected")]
    [JsonRequired]
    public string Protected { get; set; } = string.Empty;

    /// <summary>
    /// The computed signature, Base64url-encoded.
    /// </summary>
    [JsonPropertyName("signature")]
    [JsonRequired]
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// The unprotected JWS header values.
    /// </summary>
    [JsonPropertyName("header")]
    public Dictionary<string, JsonElement>? Header { get; set; }
}
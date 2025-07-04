using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents authentication information for A2A communication.
/// </summary>
public class AuthenticationInfo
{
    /// <summary>
    /// Gets or sets the list of authentication schemes supported or required.
    /// </summary>
    [JsonPropertyName("schemes")]
    [JsonRequired]
    public List<string> Schemes { get; set; } = [];

    /// <summary>
    /// Gets or sets the authentication credentials (format depends on the scheme).
    /// </summary>
    [JsonPropertyName("credentials")]
    public string? Credentials { get; set; }
}
namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a security requirement for an agent or skill.</summary>
public sealed class SecurityRequirement
{
    /// <summary>Gets or sets the security schemes and their required scopes.</summary>
    [JsonPropertyName("schemes")]
    public Dictionary<string, StringList>? Schemes { get; set; }
}

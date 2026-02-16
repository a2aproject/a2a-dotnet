namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents authentication information for push notifications.</summary>
public sealed class AuthenticationInfo
{
    /// <summary>Gets or sets the authentication scheme.</summary>
    [JsonPropertyName("scheme"), JsonRequired]
    public string Scheme { get; set; } = string.Empty;

    /// <summary>Gets or sets the credentials.</summary>
    [JsonPropertyName("credentials")]
    public string? Credentials { get; set; }
}

namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents an API key security scheme.</summary>
public sealed class ApiKeySecurityScheme
{
    /// <summary>Gets or sets the name of the API key.</summary>
    [JsonPropertyName("name"), JsonRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>Description of the API key security scheme.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Location of the API key (query, header, or cookie).</summary>
    [JsonPropertyName("location"), JsonRequired]
    public string Location { get; set; } = string.Empty;
}

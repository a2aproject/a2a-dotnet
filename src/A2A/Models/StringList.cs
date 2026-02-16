namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a list of strings, used for security requirement scope values.</summary>
public sealed class StringList
{
    /// <summary>Gets or sets the list of string values.</summary>
    [JsonPropertyName("list")]
    public List<string> List { get; set; } = [];
}

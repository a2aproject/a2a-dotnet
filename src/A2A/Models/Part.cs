namespace A2A;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Identifies which content field is set on a Part.</summary>
public enum PartContentCase
{
    /// <summary>No content field is set.</summary>
    None,
    /// <summary>Text content.</summary>
    Text,
    /// <summary>Raw binary content (base64 encoded).</summary>
    Raw,
    /// <summary>URL content.</summary>
    Url,
    /// <summary>Structured data content.</summary>
    Data,
}

/// <summary>Represents a content part in the A2A protocol. Uses field-presence to indicate the content type.</summary>
public sealed class Part
{
    /// <summary>Gets or sets the text content.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Gets or sets the raw binary content.</summary>
    [JsonPropertyName("raw")]
    public byte[]? Raw { get; set; }

    /// <summary>Gets or sets the URL reference to content.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Gets or sets the structured data content.</summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    /// <summary>Gets or sets the metadata associated with this part.</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    /// <summary>Gets or sets the filename associated with this part.</summary>
    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    /// <summary>Gets or sets the media type of the content.</summary>
    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    /// <summary>Gets which content field is currently set.</summary>
    [JsonIgnore]
    public PartContentCase ContentCase =>
        Text is not null ? PartContentCase.Text :
        Raw is not null ? PartContentCase.Raw :
        Url is not null ? PartContentCase.Url :
        Data is not null ? PartContentCase.Data :
        PartContentCase.None;

    /// <summary>Creates a text part.</summary>
    /// <param name="text">The text content.</param>
    /// <returns>A new <see cref="Part"/> with the text field set.</returns>
    public static Part FromText(string text) => new() { Text = text };

    /// <summary>Creates a part from raw binary data.</summary>
    /// <param name="raw">The raw binary content.</param>
    /// <param name="mediaType">The media type of the content.</param>
    /// <param name="filename">An optional filename.</param>
    /// <returns>A new <see cref="Part"/> with the raw field set.</returns>
    public static Part FromRaw(byte[] raw, string? mediaType = null, string? filename = null) =>
        new() { Raw = raw, MediaType = mediaType, Filename = filename };

    /// <summary>Creates a part from a URL reference.</summary>
    /// <param name="url">The URL reference.</param>
    /// <param name="mediaType">The media type of the content.</param>
    /// <param name="filename">An optional filename.</param>
    /// <returns>A new <see cref="Part"/> with the URL field set.</returns>
    public static Part FromUrl(string url, string? mediaType = null, string? filename = null) =>
        new() { Url = url, MediaType = mediaType, Filename = filename };

    /// <summary>Creates a part from structured data.</summary>
    /// <param name="data">The structured data.</param>
    /// <returns>A new <see cref="Part"/> with the data field set.</returns>
    public static Part FromData(JsonElement data) => new() { Data = data };
}
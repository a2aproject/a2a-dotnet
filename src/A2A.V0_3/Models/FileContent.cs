using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.V0_3;

/// <summary>
/// Represents the base entity for FileParts.
/// According to the A2A spec, FileContent types are distinguished by the presence
/// of either "bytes" or "uri" properties, not by a discriminator.
/// </summary>
public class FileContent
{
    private string? _bytes;
    private Uri? _uri;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileContent"/> class.
    /// </summary>
    [JsonConstructor(), Obsolete("Parameterless ctor is only for Json de/serialization. Use a constructor specifying 'bytes' or 'uri'.")]
    public FileContent() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileContent"/> class with base64-encoded bytes.
    /// </summary>
    /// <param name="bytes">The base64-encoded string representing the file content.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bytes"/> is null or whitespace.</exception>
    public FileContent(string bytes)
    {
        if (string.IsNullOrWhiteSpace(bytes))
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        _bytes = bytes;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileContent"/> class from a byte sequence.
    /// </summary>
    /// <param name="bytes">The byte sequence representing the file content.</param>
    /// <param name="encoding">The encoding to use for converting bytes to a string. Defaults to UTF-8 if not specified.</param>
    public FileContent(IEnumerable<byte> bytes, Encoding? encoding = null) => _bytes = (encoding ?? Encoding.UTF8).GetString([.. bytes]);

    /// <summary>
    /// Initializes a new instance of the <see cref="FileContent"/> class with a URI reference.
    /// </summary>
    /// <param name="uri">The URI pointing to the file content.</param>
    public FileContent(Uri uri) => _uri = uri;

    /// <summary>
    /// Optional name for the file.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Optional mimeType for the file.
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// base64 encoded content of the file.
    /// </summary>
    [JsonPropertyName("bytes")]
    public string? Bytes
    {
        get => _bytes;
        set
        {
            if (!string.IsNullOrWhiteSpace(_uri?.ToString()))
            {
                throw new A2AException("Only one of 'bytes' or 'uri' must be specified", A2AErrorCode.InvalidRequest);
            }

            _bytes = value;
        }
    }

    /// <summary>
    /// URL for the File content.
    /// </summary>
    [JsonPropertyName("uri")]
    public Uri? Uri
    {
        get => _uri;
        set
        {
            if (!string.IsNullOrWhiteSpace(_bytes))
            {
                throw new A2AException("Only one of 'bytes' or 'uri' must be specified", A2AErrorCode.InvalidRequest);
            }

            _uri = value;
        }
    }

    internal class Converter : A2AJsonConverter<FileContent>
    {
        protected override FileContent? DeserializeImpl(Type typeToConvert, JsonSerializerOptions options, JsonDocument document)
        {
            var root = document.RootElement;

            // Determine type based on presence of required properties
            bool hasBytes = root.TryGetProperty("bytes", out var bytesProperty) &&
                           bytesProperty.ValueKind == JsonValueKind.String;
            bool hasUri = root.TryGetProperty("uri", out var uriProperty) &&
                         uriProperty.ValueKind == JsonValueKind.String;

            if (!hasBytes && !hasUri)
            {
                throw new A2AException("FileContent must have either 'bytes' or 'uri' property", A2AErrorCode.InvalidRequest);
            }

            return base.DeserializeImpl(typeof(FileContent), options, document);
        }
    }
}

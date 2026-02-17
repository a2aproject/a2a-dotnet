using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents the base entity for FileParts.
/// According to the A2A spec, FileContent types are distinguished by the presence
/// of either "fileWithBytes" or "fileWithUri" properties, not by a discriminator.
/// </summary>
public class FileContent
{
    private string? _fileWithBytes;
    private Uri? _fileWithUri;

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

        _fileWithBytes = bytes;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileContent"/> class from a byte sequence.
    /// </summary>
    /// <param name="bytes">The byte sequence representing the file content.</param>
    /// <param name="encoding">The encoding to use for converting bytes to a string. Defaults to UTF-8 if not specified.</param>
    public FileContent(IEnumerable<byte> bytes, Encoding? encoding = null) => _fileWithBytes = (encoding ?? Encoding.UTF8).GetString([.. bytes]);

    /// <summary>
    /// Initializes a new instance of the <see cref="FileContent"/> class with a URI reference.
    /// </summary>
    /// <param name="uri">The URI pointing to the file content.</param>
    public FileContent(Uri uri) => _fileWithUri = uri;

    /// <summary>
    /// Optional name for the file.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Optional media type for the file.
    /// </summary>
    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    /// <summary>
    /// base64 encoded content of the file.
    /// </summary>
    [JsonPropertyName("fileWithBytes")]
    public string? FileWithBytes
    {
        get => _fileWithBytes;
        set
        {
            if (!string.IsNullOrWhiteSpace(_fileWithUri?.ToString()))
            {
                throw new A2AException("Only one of 'fileWithBytes' or 'fileWithUri' must be specified", A2AErrorCode.InvalidRequest);
            }

            _fileWithBytes = value;
        }
    }

    /// <summary>
    /// URL for the File content.
    /// </summary>
    [JsonPropertyName("fileWithUri")]
    public Uri? FileWithUri
    {
        get => _fileWithUri;
        set
        {
            if (!string.IsNullOrWhiteSpace(_fileWithBytes))
            {
                throw new A2AException("Only one of 'fileWithBytes' or 'fileWithUri' must be specified", A2AErrorCode.InvalidRequest);
            }

            _fileWithUri = value;
        }
    }

    internal class Converter : A2AJsonConverter<FileContent>
    {
        protected override FileContent? DeserializeImpl(Type typeToConvert, JsonSerializerOptions options, JsonDocument document)
        {
            var root = document.RootElement;

            // Determine type based on presence of required properties
            bool hasBytes = root.TryGetProperty("fileWithBytes", out var bytesProperty) &&
                           bytesProperty.ValueKind == JsonValueKind.String;
            bool hasUri = root.TryGetProperty("fileWithUri", out var uriProperty) &&
                         uriProperty.ValueKind == JsonValueKind.String;

            if (!hasBytes && !hasUri)
            {
                throw new A2AException("FileContent must have either 'fileWithBytes' or 'fileWithUri' property", A2AErrorCode.InvalidRequest);
            }

            return base.DeserializeImpl(typeof(FileContent), options, document);
        }
    }
}

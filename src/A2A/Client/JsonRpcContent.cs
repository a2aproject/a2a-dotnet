using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>
/// HTTP content for JSON-RPC requests and responses.
/// </summary>
public sealed class JsonRpcContent : HttpContent
{
    private readonly object? _contentToSerialize;
    private readonly JsonTypeInfo _contentTypeInfo;

    /// <summary>
    /// Initializes a new instance of JsonRpcContent for a JSON-RPC request.
    /// </summary>
    /// <param name="request">The JSON-RPC request to serialize.</param>
    public JsonRpcContent(JsonRpcRequest? request) : this(request, A2AJsonUtilities.JsonContext.Default.JsonRpcRequest)
    {
    }

    /// <summary>
    /// Initializes a new instance of JsonRpcContent for a JSON-RPC response.
    /// </summary>
    /// <param name="response">The JSON-RPC response to serialize.</param>
    public JsonRpcContent(JsonRpcResponse? response) : this(response, A2AJsonUtilities.JsonContext.Default.JsonRpcResponse)
    {
    }

    /// <summary>
    /// Initializes a new instance of JsonRpcContent with custom content and type info.
    /// </summary>
    /// <param name="contentToSerialize">The content to serialize.</param>
    /// <param name="contentTypeInfo">Type information for serialization.</param>
    private JsonRpcContent(object? contentToSerialize, JsonTypeInfo contentTypeInfo)
    {
        _contentToSerialize = contentToSerialize;
        _contentTypeInfo = contentTypeInfo;
        Headers.TryAddWithoutValidation("Content-Type", "application/json");
    }

    /// <inheritdoc />
    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    /// <inheritdoc />
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        JsonSerializer.SerializeAsync(stream, _contentToSerialize, _contentTypeInfo);

#if NET8_0_OR_GREATER
    /// <inheritdoc />
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
        JsonSerializer.SerializeAsync(stream, _contentToSerialize, _contentTypeInfo, cancellationToken);

    /// <inheritdoc />
    protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(stream, _contentToSerialize, _contentTypeInfo);
#endif
}
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

public sealed class JsonRpcContent : HttpContent
{
    private readonly object? _contentToSerialize;
    private readonly JsonTypeInfo _contentTypeInfo;

    public JsonRpcContent(JsonRpcRequest? request) : this(request, A2AJsonUtilities.JsonContext.Default.JsonRpcRequest)
    {
    }

    public JsonRpcContent(JsonRpcResponse? response) : this(response, A2AJsonUtilities.JsonContext.Default.JsonRpcResponse)
    {
    }

    private JsonRpcContent(object? contentToSerialize, JsonTypeInfo contentTypeInfo)
    {
        _contentToSerialize = contentToSerialize;
        _contentTypeInfo = contentTypeInfo;
        Headers.TryAddWithoutValidation("Content-Type", "application/json");
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        JsonSerializer.SerializeAsync(stream, _contentToSerialize, _contentTypeInfo);

#if NET8_0_OR_GREATER
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
        JsonSerializer.SerializeAsync(stream, _contentToSerialize, _contentTypeInfo, cancellationToken);

    protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(stream, _contentToSerialize, _contentTypeInfo);
#endif
}
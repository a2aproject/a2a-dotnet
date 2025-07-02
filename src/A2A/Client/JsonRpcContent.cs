using System.Net;
using System.Text.Json;

namespace A2A;

public class JsonRpcContent : HttpContent
{
    private readonly Stream stream;

    public JsonRpcContent(JsonRpcRequest request)
    {
        Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        // Serialize the request directly to the stream
        stream = new MemoryStream();
        JsonSerializer.Serialize(stream, request, A2AJsonUtilities.JsonContext.Default.JsonRpcRequest);
        stream.Position = 0;
    }

    public JsonRpcContent(JsonRpcResponse response)
    {
        Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        // Serialize the response directly to the stream
        stream = new MemoryStream();
        JsonSerializer.Serialize(stream, response, A2AJsonUtilities.JsonContext.Default.JsonRpcResponse);
        stream.Position = 0;
    }

#if NET8_0_OR_GREATER
    protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        this.stream.CopyTo(stream);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        await this.stream.CopyToAsync(stream, cancellationToken);
    }
#endif

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        this.stream.CopyTo(stream);
        return Task.CompletedTask;
    }

    protected override bool TryComputeLength(out long length)
    {
        length = stream.Length;
        return true;
    }
}
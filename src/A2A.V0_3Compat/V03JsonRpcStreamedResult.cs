namespace A2A.V0_3Compat;

using Microsoft.AspNetCore.Http;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using V03 = A2A.V0_3;

/// <summary>Result type for streaming v0.3-format JSON-RPC responses as SSE.</summary>
internal sealed class V03JsonRpcStreamedResult : IResult
{
    private readonly IAsyncEnumerable<V03.A2AEvent> _events;
    private readonly V03.JsonRpcId _requestId;

    internal V03JsonRpcStreamedResult(IAsyncEnumerable<V03.A2AEvent> events, V03.JsonRpcId requestId)
    {
        ArgumentNullException.ThrowIfNull(events);
        _events = events;
        _requestId = requestId;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.Append("Cache-Control", "no-cache");

        var responseTypeInfo = V03.A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(V03.JsonRpcResponse));
        var eventTypeInfo = V03.A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(V03.A2AEvent));

        try
        {
            await SseFormatter.WriteAsync(
                _events.Select(e => new SseItem<V03.JsonRpcResponse>(
                    V03.JsonRpcResponse.CreateJsonRpcResponse(_requestId, e, eventTypeInfo))),
                httpContext.Response.Body,
                (item, writer) =>
                {
                    using Utf8JsonWriter json = new(writer, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                    JsonSerializer.Serialize(json, item.Data, responseTypeInfo);
                },
                httpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — expected
        }
        catch (Exception ex)
        {
            try
            {
                var errorResponse = ex is A2AException a2aEx
                    ? V03.JsonRpcResponse.CreateJsonRpcErrorResponse(_requestId,
                        new V03.A2AException(a2aEx.Message, (V03.A2AErrorCode)(int)a2aEx.ErrorCode))
                    : V03.JsonRpcResponse.InternalErrorResponse(_requestId, "An internal error occurred during streaming.");
                var errorJson = JsonSerializer.Serialize(errorResponse, responseTypeInfo);
                var errorBytes = Encoding.UTF8.GetBytes($"data: {errorJson}\n\n");
                await httpContext.Response.Body.WriteAsync(errorBytes, httpContext.RequestAborted);
                await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
            }
            catch
            {
                // Response body is no longer writable — silently abandon
            }
        }
    }
}

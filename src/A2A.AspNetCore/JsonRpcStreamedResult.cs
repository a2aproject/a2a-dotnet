using Microsoft.AspNetCore.Http;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace A2A.AspNetCore;

/// <summary>
/// Result type for streaming JSON-RPC responses as Server-Sent Events (SSE) in HTTP responses.
/// </summary>
public sealed class JsonRpcStreamedResult : IResult
{
    private readonly IAsyncEnumerable<StreamResponse> _events;
    private readonly JsonRpcId _requestId;

    /// <summary>Initializes a new instance of the <see cref="JsonRpcStreamedResult"/> class.</summary>
    /// <param name="events">The stream of response events.</param>
    /// <param name="requestId">The JSON-RPC request ID.</param>
    public JsonRpcStreamedResult(IAsyncEnumerable<StreamResponse> events, JsonRpcId requestId)
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

        var responseTypeInfo = A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcResponse));
        try
        {
            await SseFormatter.WriteAsync(
                _events.Select(e => new SseItem<JsonRpcResponse>(JsonRpcResponse.CreateJsonRpcResponse(_requestId, e))),
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
        catch (Exception)
        {
            // Stream error — response already started, cannot change status code.
            // Best effort: write an error event if the response body is still writable.
            try
            {
                var errorResponse = JsonRpcResponse.InternalErrorResponse(
                    _requestId, "An internal error occurred during streaming.");
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
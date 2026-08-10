using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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

        // Disable response buffering so heartbeat and event frames flush immediately.
        httpContext.Features.GetRequiredFeature<IHttpResponseBodyFeature>().DisableBuffering();

        // SseStreamWriter emits periodic keep-alive comment frames and per-event ids (BUG-09).
        await using var writer = new SseStreamWriter(httpContext);
        var responseTypeInfo = A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcResponse));
        try
        {
            await foreach (var ev in _events.WithCancellation(httpContext.RequestAborted).ConfigureAwait(false))
            {
                var response = JsonRpcResponse.CreateJsonRpcResponse(_requestId, ev);
                var json = JsonSerializer.Serialize(response, responseTypeInfo);
                await writer.WriteEventAsync(json, httpContext.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — expected
        }
        catch (Exception ex)
        {
            // Stream error — response already started, cannot change status code.
            // Best effort: write an error event if the response body is still writable.
            // Preserve A2AException error codes; fall back to -32603 for unexpected errors.
            try
            {
                var errorResponse = ex is A2AException a2aEx
                    ? JsonRpcResponse.CreateJsonRpcErrorResponse(_requestId, a2aEx)
                    : JsonRpcResponse.InternalErrorResponse(
                        _requestId, "An internal error occurred during streaming.");
                var errorJson = JsonSerializer.Serialize(errorResponse, responseTypeInfo);
                await writer.WriteEventAsync(errorJson, httpContext.RequestAborted);
            }
            catch
            {
                // Response body is no longer writable — silently abandon
            }
        }
    }
}
using Microsoft.AspNetCore.Http;
using System.Net.ServerSentEvents;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace A2A.AspNetCore;

/// <summary>
/// Result type for streaming JSON-RPC responses as Server-Sent Events (SSE) in HTTP responses.
/// </summary>
/// <remarks>
/// Implements IResult to provide real-time streaming of JSON-RPC responses for continuous
/// event streams like task updates, status changes, and artifact notifications.
/// </remarks>
public class JsonRpcStreamedResult : IResult
{
    private readonly IAsyncEnumerable<A2AEvent> _events;
    private readonly string? requestId;

    /// <summary>
    /// Initializes a new instance of the JsonRpcStreamedResult class.
    /// </summary>
    /// <param name="events">The async enumerable stream of A2A events to send as Server-Sent Events.</param>
    /// <param name="requestId">The JSON-RPC request ID used for correlating responses with the original request.</param>
    public JsonRpcStreamedResult(IAsyncEnumerable<A2AEvent> events, string? requestId)
    {
        ArgumentNullException.ThrowIfNull(events);

        _events = events;
        this.requestId = requestId;
    }

    /// <summary>
    /// Executes the result by streaming JSON-RPC responses as Server-Sent Events to the HTTP response.
    /// </summary>
    /// <remarks>
    /// Sets appropriate SSE headers, wraps each A2A event in a JSON-RPC response format,
    /// and streams them using the SSE protocol with proper formatting and encoding.
    /// </remarks>
    /// <param name="httpContext">The HTTP context to stream the responses to.</param>
    /// <returns>A task representing the asynchronous streaming operation.</returns>
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.Append("Cache-Control", "no-cache");

        var responseTypeInfo = A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcResponse));
        await SseFormatter.WriteAsync(
            _events.Select(e => new SseItem<JsonRpcResponse>(JsonRpcResponse.CreateJsonRpcResponse(requestId, e))),
            httpContext.Response.Body,
            (item, writer) =>
            {
                using Utf8JsonWriter json = new(writer, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                JsonSerializer.Serialize(json, item.Data, responseTypeInfo);
            },
            httpContext.RequestAborted);
    }
}
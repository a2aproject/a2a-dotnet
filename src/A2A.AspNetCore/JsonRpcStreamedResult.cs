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

        IAsyncEnumerator<StreamResponse> enumerator;
        try
        {
            enumerator = _events.GetAsyncEnumerator(httpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            await WriteErrorAsync(httpContext, ex, streamStarted: false).ConfigureAwait(false);
            return;
        }

        Exception? failure = null;
        SseStreamWriter? sseWriter = null;
        var streamStarted = false;
        var completedWithoutEvents = false;
        try
        {
            if (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                ConfigureSseResponse(httpContext);
                streamStarted = true;
                sseWriter = new SseStreamWriter(httpContext);

                var responseTypeInfo = A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcResponse));
                do
                {
                    var response = JsonRpcResponse.CreateJsonRpcResponse(
                        _requestId, enumerator.Current);
                    #pragma warning disable VSTHRD103 // Serialize to string is not blocking I/O
                    var json = JsonSerializer.Serialize(response, responseTypeInfo);
                    #pragma warning restore VSTHRD103
                    await sseWriter.WriteEventAsync(
                        json, httpContext.RequestAborted).ConfigureAwait(false);
                }
                while (await enumerator.MoveNextAsync().ConfigureAwait(false));
            }
            else
            {
                completedWithoutEvents = true;
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — expected
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            try
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected — expected
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        try
        {
            if (failure is not null)
            {
                await WriteErrorAsync(
                    httpContext, failure, streamStarted, sseWriter).ConfigureAwait(false);
            }
            else if (completedWithoutEvents)
            {
                ConfigureSseResponse(httpContext);
            }
        }
        finally
        {
            if (sseWriter is not null)
            {
                await sseWriter.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static void ConfigureSseResponse(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.Append("Cache-Control", "no-cache");
        httpContext.Features.GetRequiredFeature<IHttpResponseBodyFeature>().DisableBuffering();
    }

    private async Task WriteErrorAsync(
        HttpContext httpContext,
        Exception exception,
        bool streamStarted,
        SseStreamWriter? sseWriter = null)
    {
        var errorResponse = CreateErrorResponse(
            exception,
            streamStarted
                ? "An internal error occurred during streaming."
                : "An internal error occurred.");

        if (!streamStarted)
        {
            await new JsonRpcResponseResult(errorResponse).ExecuteAsync(httpContext).ConfigureAwait(false);
            return;
        }

        try
        {
            if (sseWriter is null)
            {
                throw new InvalidOperationException("The SSE writer was not initialized.");
            }

            var responseTypeInfo = A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcResponse));
            #pragma warning disable VSTHRD103 // Serialize to string is not blocking I/O
            var errorJson = JsonSerializer.Serialize(errorResponse, responseTypeInfo);
            #pragma warning restore VSTHRD103
            await sseWriter.WriteEventAsync(
                errorJson, httpContext.RequestAborted).ConfigureAwait(false);
        }
        catch
        {
            // Response body is no longer writable — silently abandon
        }
    }

    private JsonRpcResponse CreateErrorResponse(Exception exception, string internalErrorMessage) =>
        exception is A2AException a2aException
            ? JsonRpcResponse.CreateJsonRpcErrorResponse(_requestId, a2aException)
            : JsonRpcResponse.InternalErrorResponse(_requestId, internalErrorMessage);
}
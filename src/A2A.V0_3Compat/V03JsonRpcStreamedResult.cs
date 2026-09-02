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

        var responseTypeInfo = V03.A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(V03.JsonRpcResponse));
        var eventTypeInfo = V03.A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(V03.A2AEvent));

        IAsyncEnumerator<V03.A2AEvent> enumerator;
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
            await WriteErrorAsync(httpContext, ex, streamStarted: false, responseTypeInfo).ConfigureAwait(false);
            return;
        }

        Exception? failure = null;
        var streamStarted = false;
        var completedWithoutEvents = false;
        try
        {
            if (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                ConfigureSseResponse(httpContext);
                streamStarted = true;

                await SseFormatter.WriteAsync(
                    EnumerateFromCurrentAsync(enumerator).Select(e => new SseItem<V03.JsonRpcResponse>(
                        V03.JsonRpcResponse.CreateJsonRpcResponse(_requestId, e, eventTypeInfo))),
                    httpContext.Response.Body,
                    (item, writer) =>
                    {
                        using Utf8JsonWriter json = new(writer, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                        JsonSerializer.Serialize(json, item.Data, responseTypeInfo);
                    },
                    httpContext.RequestAborted).ConfigureAwait(false);
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

        if (failure is not null)
        {
            await WriteErrorAsync(httpContext, failure, streamStarted, responseTypeInfo).ConfigureAwait(false);
        }
        else if (completedWithoutEvents)
        {
            ConfigureSseResponse(httpContext);
        }
    }

    private static void ConfigureSseResponse(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.Append("Cache-Control", "no-cache");
    }

    private async Task WriteErrorAsync(
        HttpContext httpContext,
        Exception exception,
        bool streamStarted,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo responseTypeInfo)
    {
        var errorResponse = exception is A2AException a2aException
            ? V03.JsonRpcResponse.CreateJsonRpcErrorResponse(
                _requestId,
                new V03.A2AException(a2aException.Message, (V03.A2AErrorCode)(int)a2aException.ErrorCode))
            : V03.JsonRpcResponse.InternalErrorResponse(
                _requestId,
                streamStarted
                    ? "An internal error occurred during streaming."
                    : "An internal error occurred.");

        if (!streamStarted)
        {
            await new V03JsonRpcResponseResult(errorResponse).ExecuteAsync(httpContext).ConfigureAwait(false);
            return;
        }

        try
        {
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

    private static async IAsyncEnumerable<V03.A2AEvent> EnumerateFromCurrentAsync(
        IAsyncEnumerator<V03.A2AEvent> enumerator)
    {
        do
        {
            yield return enumerator.Current;
        }
        while (await enumerator.MoveNextAsync().ConfigureAwait(false));
    }
}

using Microsoft.AspNetCore.Http;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A.AspNetCore;

internal sealed class CustomJsonRpcStreamedResult(
    IAsyncEnumerable<object?> events,
    JsonTypeInfo eventTypeInfo,
    JsonRpcId requestId) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        IAsyncEnumerator<object?> enumerator;
        try
        {
            enumerator = events.GetAsyncEnumerator(httpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            await WriteErrorAsync(httpContext, exception, streamStarted: false).ConfigureAwait(false);
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
                var responseTypeInfo = A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcResponse));

                await SseFormatter.WriteAsync(
                    EnumerateFromCurrentAsync(enumerator)
                        .Select(streamEvent => new SseItem<JsonRpcResponse>(
                            JsonRpcResponse.CreateJsonRpcResponse(
                                requestId,
                                streamEvent,
                                eventTypeInfo))),
                    httpContext.Response.Body,
                    (item, writer) =>
                    {
                        using Utf8JsonWriter json = new(
                            writer,
                            new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
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
            // Client disconnected.
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected.
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }

        if (failure is not null)
        {
            await WriteErrorAsync(httpContext, failure, streamStarted).ConfigureAwait(false);
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

    private async Task WriteErrorAsync(HttpContext httpContext, Exception exception, bool streamStarted)
    {
        var errorResponse = exception is A2AException a2aException
            ? JsonRpcResponse.CreateJsonRpcErrorResponse(requestId, a2aException)
            : JsonRpcResponse.InternalErrorResponse(
                requestId,
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
            var responseTypeInfo = A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcResponse));
#pragma warning disable VSTHRD103
            var errorJson = JsonSerializer.Serialize(errorResponse, responseTypeInfo);
#pragma warning restore VSTHRD103
            var errorBytes = Encoding.UTF8.GetBytes($"data: {errorJson}\n\n");
            await httpContext.Response.Body.WriteAsync(errorBytes, httpContext.RequestAborted).ConfigureAwait(false);
            await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted).ConfigureAwait(false);
        }
        catch
        {
            // The response body is no longer writable.
        }
    }

    private static async IAsyncEnumerable<object?> EnumerateFromCurrentAsync(
        IAsyncEnumerator<object?> enumerator)
    {
        do
        {
            yield return enumerator.Current;
        }
        while (await enumerator.MoveNextAsync().ConfigureAwait(false));
    }
}

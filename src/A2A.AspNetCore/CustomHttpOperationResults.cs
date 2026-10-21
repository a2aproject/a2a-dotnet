using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A.AspNetCore;

internal sealed class CustomHttpJsonResult(
    object? value,
    JsonTypeInfo typeInfo) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            value,
            typeInfo,
            httpContext.RequestAborted).ConfigureAwait(false);
    }
}

internal sealed class CustomHttpStreamedResult(
    IAsyncEnumerable<object?> events,
    JsonTypeInfo eventTypeInfo,
    ILogger logger) : IResult
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
                await SseFormatter.WriteAsync(
                    EnumerateFromCurrentAsync(enumerator)
                        .Select(streamEvent => new SseItem<object?>(streamEvent)),
                    httpContext.Response.Body,
                    (item, writer) =>
                    {
                        using Utf8JsonWriter json = new(
                            writer,
                            new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                        JsonSerializer.Serialize(json, item.Data, eventTypeInfo);
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
        httpContext.Response.Headers.CacheControl = "no-cache,no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
        httpContext.Response.Headers.ContentEncoding = "identity";

        var bufferingFeature = httpContext.Features.GetRequiredFeature<IHttpResponseBodyFeature>();
        bufferingFeature.DisableBuffering();
    }

    private async Task WriteErrorAsync(
        HttpContext httpContext,
        Exception exception,
        bool streamStarted)
    {
        var mappedException = exception as A2AException;
        if (mappedException is null)
        {
            logger.UnexpectedErrorInActivityName(exception, "custom HTTP streaming operation");
            mappedException = new A2AException(
                streamStarted
                    ? "An internal error occurred during streaming."
                    : "An internal error occurred.",
                A2AErrorCode.InternalError);
        }

        if (!streamStarted)
        {
            await new A2AErrorResult(mappedException).ExecuteAsync(httpContext).ConfigureAwait(false);
            return;
        }

        try
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStringValue(mappedException.Message);
            }

            var messageJson = Encoding.UTF8.GetString(buffer.ToArray());
            var errorBytes = Encoding.UTF8.GetBytes(
                $"event: error\ndata: {messageJson}\n\n");
            await httpContext.Response.Body
                .WriteAsync(errorBytes, httpContext.RequestAborted)
                .ConfigureAwait(false);
            await httpContext.Response.Body
                .FlushAsync(httpContext.RequestAborted)
                .ConfigureAwait(false);
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

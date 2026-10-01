using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A.AspNetCore;

/// <summary>
/// Result type for streaming JSON-RPC responses as Server-Sent Events (SSE) in HTTP responses.
/// </summary>
public sealed class JsonRpcStreamedResult : IResult
{
    private readonly IAsyncEnumerable<JsonRpcResponse> _responses;
    private readonly JsonRpcId _requestId;
    private readonly Func<Exception, string, JsonRpcResponse> _createErrorResponse;
    private readonly Activity? _operationActivity;
    private readonly Activity? _transportActivity;

    /// <summary>Initializes a new instance of the <see cref="JsonRpcStreamedResult"/> class.</summary>
    /// <param name="events">The stream of response events.</param>
    /// <param name="requestId">The JSON-RPC request ID.</param>
    public JsonRpcStreamedResult(
        IAsyncEnumerable<StreamResponse> events,
        JsonRpcId requestId)
        : this(
            CreateResponsesAsync(
                events,
                requestId,
                (JsonTypeInfo<StreamResponse>)A2AJsonUtilities.DefaultOptions
                    .GetTypeInfo(typeof(StreamResponse))),
            requestId,
            (exception, internalErrorMessage) =>
                CreateStandardErrorResponse(
                    requestId,
                    exception,
                    internalErrorMessage),
            operationActivity: null)
    {
        ArgumentNullException.ThrowIfNull(events);
    }

    private JsonRpcStreamedResult(
        IAsyncEnumerable<JsonRpcResponse> responses,
        JsonRpcId requestId,
        Func<Exception, string, JsonRpcResponse> createErrorResponse,
        Activity? operationActivity,
        Activity? transportActivity = null)
    {
        _responses = responses;
        _requestId = requestId;
        _createErrorResponse = createErrorResponse;
        _operationActivity = operationActivity;
        _transportActivity = transportActivity;
    }

    internal static JsonRpcStreamedResult Create<TEvent>(
        IAsyncEnumerable<TEvent> events,
        JsonRpcId requestId,
        JsonTypeInfo<TEvent> eventTypeInfo,
        Func<JsonRpcId, Exception, string, JsonRpcResponse> createErrorResponse,
        Activity? operationActivity,
        Activity? transportActivity = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(eventTypeInfo);
        ArgumentNullException.ThrowIfNull(createErrorResponse);

        return new JsonRpcStreamedResult(
            CreateResponsesAsync(events, requestId, eventTypeInfo),
            requestId,
            (exception, internalErrorMessage) =>
                createErrorResponse(
                    requestId,
                    exception,
                    internalErrorMessage),
            operationActivity,
            transportActivity);
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var operationActivity = _operationActivity;
        IAsyncEnumerator<JsonRpcResponse> enumerator;
        try
        {
            enumerator = _responses.GetAsyncEnumerator(
                httpContext.RequestAborted);
        }
        catch (OperationCanceledException ex)
            when (httpContext.RequestAborted.IsCancellationRequested)
        {
            A2AOperationDiagnostics.SetError(operationActivity, ex, httpContext.RequestAborted);
            return;
        }
        catch (Exception ex)
        {
            A2AOperationDiagnostics.SetError(operationActivity, ex, httpContext.RequestAborted);
            await WriteErrorAsync(
                httpContext,
                ex,
                streamStarted: false).ConfigureAwait(false);
            return;
        }

        Exception? failure = null;
        var streamStarted = false;
        var completedWithoutEvents = false;
        OperationCanceledException? cancellation = null;
        try
        {
            if (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                ConfigureSseResponse(httpContext);
                streamStarted = true;

                var responseTypeInfo = A2AJsonUtilities.DefaultOptions
                    .GetTypeInfo(typeof(JsonRpcResponse));
                await SseFormatter.WriteAsync(
                    EnumerateFromCurrentAsync(enumerator)
                        .Select(static response =>
                            new SseItem<JsonRpcResponse>(response)),
                    httpContext.Response.Body,
                    (item, writer) =>
                    {
                        using Utf8JsonWriter json = new(
                            writer,
                            new()
                            {
                                Encoder =
                                    JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                            });
                        JsonSerializer.Serialize(
                            json,
                            item.Data,
                            responseTypeInfo);
                    },
                    httpContext.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                completedWithoutEvents = true;
            }
        }
        catch (OperationCanceledException ex)
        {
            cancellation = ex;
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
            catch (OperationCanceledException ex)
            {
                cancellation = ex;
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure is not null)
        {
            A2AOperationDiagnostics.SetError(operationActivity, failure, httpContext.RequestAborted);
            await WriteErrorAsync(
                httpContext,
                failure,
                streamStarted).ConfigureAwait(false);
        }
        else if (cancellation is not null)
        {
            A2AOperationDiagnostics.SetError(operationActivity, cancellation, httpContext.RequestAborted);
            if (!httpContext.RequestAborted.IsCancellationRequested)
            {
                await WriteErrorAsync(
                    httpContext,
                    cancellation,
                    streamStarted).ConfigureAwait(false);
            }
        }
        else
        {
            if (completedWithoutEvents)
            {
                ConfigureSseResponse(httpContext);
            }

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
        bool streamStarted)
    {
        A2AAspNetCoreDiagnostics.RecordException(_transportActivity, exception);
        JsonRpcResponse errorResponse;
        try
        {
            errorResponse = _createErrorResponse(
                exception,
                streamStarted
                    ? "An internal error occurred during streaming."
                    : "An internal error occurred.");
        }
        catch (Exception)
        {
            errorResponse = JsonRpcResponse.InternalErrorResponse(
                _requestId,
                streamStarted
                    ? "An internal error occurred during streaming."
                    : "An internal error occurred.");
        }

        if (!streamStarted)
        {
            await new JsonRpcResponseResult(errorResponse)
                .ExecuteAsync(httpContext).ConfigureAwait(false);
            return;
        }

        try
        {
            var responseTypeInfo = A2AJsonUtilities.DefaultOptions
                .GetTypeInfo(typeof(JsonRpcResponse));
#pragma warning disable VSTHRD103
            var errorJson = JsonSerializer.Serialize(
                errorResponse,
                responseTypeInfo);
#pragma warning restore VSTHRD103
            var errorBytes = Encoding.UTF8.GetBytes(
                $"data: {errorJson}\n\n");
            await httpContext.Response.Body.WriteAsync(
                errorBytes,
                httpContext.RequestAborted);
            await httpContext.Response.Body.FlushAsync(
                httpContext.RequestAborted);
        }
        catch (OperationCanceledException)
            when (httpContext.RequestAborted.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
    }

    private static JsonRpcResponse CreateStandardErrorResponse(
        JsonRpcId requestId,
        Exception exception,
        string internalErrorMessage) =>
        exception is A2AException a2aException
            ? JsonRpcResponse.CreateJsonRpcErrorResponse(
                requestId,
                a2aException)
            : JsonRpcResponse.InternalErrorResponse(
                requestId,
                internalErrorMessage);

    private static async IAsyncEnumerable<JsonRpcResponse> CreateResponsesAsync<TEvent>(
        IAsyncEnumerable<TEvent> events,
        JsonRpcId requestId,
        JsonTypeInfo<TEvent> eventTypeInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in events
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return JsonRpcResponse.CreateJsonRpcResponse(
                requestId,
                item,
                eventTypeInfo);
        }
    }

    private static async IAsyncEnumerable<JsonRpcResponse>
        EnumerateFromCurrentAsync(
            IAsyncEnumerator<JsonRpcResponse> enumerator)
    {
        do
        {
            yield return enumerator.Current;
        }
        while (await enumerator.MoveNextAsync().ConfigureAwait(false));
    }
}

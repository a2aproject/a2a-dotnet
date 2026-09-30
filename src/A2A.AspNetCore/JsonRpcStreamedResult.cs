using Microsoft.AspNetCore.Http;
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
    private readonly A2AOperationDiagnosticContext? _diagnosticContext;

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
            diagnosticContext: null)
    {
        ArgumentNullException.ThrowIfNull(events);
    }

    private JsonRpcStreamedResult(
        IAsyncEnumerable<JsonRpcResponse> responses,
        JsonRpcId requestId,
        Func<Exception, string, JsonRpcResponse> createErrorResponse,
        A2AOperationDiagnosticContext? diagnosticContext)
    {
        _responses = responses;
        _requestId = requestId;
        _createErrorResponse = createErrorResponse;
        _diagnosticContext = diagnosticContext;
    }

    internal static JsonRpcStreamedResult Create<TEvent>(
        IAsyncEnumerable<TEvent> events,
        JsonRpcId requestId,
        JsonTypeInfo<TEvent> eventTypeInfo,
        Func<JsonRpcId, Exception, string, JsonRpcResponse> createErrorResponse,
        A2AOperationDiagnosticContext diagnosticContext)
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
            diagnosticContext);
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        using var operationActivity = _diagnosticContext?.Start();
        IAsyncEnumerator<JsonRpcResponse> enumerator;
        try
        {
            enumerator = _responses.GetAsyncEnumerator(
                httpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            A2AOperationDiagnostics.SetOutcome(operationActivity, "cancelled");
            return;
        }
        catch (Exception ex)
        {
            A2AOperationDiagnostics.SetError(operationActivity, ex);
            await WriteErrorAsync(
                httpContext,
                ex,
                streamStarted: false).ConfigureAwait(false);
            return;
        }

        Exception? failure = null;
        var streamStarted = false;
        var completedWithoutEvents = false;
        var cancelled = false;
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
        catch (OperationCanceledException)
        {
            cancelled = true;
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
                cancelled = true;
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure is not null)
        {
            A2AOperationDiagnostics.SetError(operationActivity, failure);
            await WriteErrorAsync(
                httpContext,
                failure,
                streamStarted).ConfigureAwait(false);
        }
        else if (cancelled)
        {
            A2AOperationDiagnostics.SetOutcome(operationActivity, "cancelled");
        }
        else
        {
            if (completedWithoutEvents)
            {
                ConfigureSseResponse(httpContext);
            }

            A2AOperationDiagnostics.SetOutcome(operationActivity, "success");
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

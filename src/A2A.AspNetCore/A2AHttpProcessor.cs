using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A.AspNetCore;

/// <summary>
/// Static processor class for handling A2A HTTP requests in ASP.NET Core applications.
/// </summary>
internal static class A2AHttpProcessor
{
    internal static Task<IResult> GetTaskAsync(IA2ARequestHandler requestHandler, ILogger logger, string id, int? historyLength, string? metadata, CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(logger, "GetTask", async ct =>
        {
            var agentTask = await requestHandler.GetTaskAsync(new GetTaskRequest
            {
                Id = id,
                HistoryLength = historyLength,
            }, ct).ConfigureAwait(false);

            return new JsonRpcResponseResult(JsonRpcResponse.CreateJsonRpcResponse(new JsonRpcId("http"), agentTask));
        }, id, cancellationToken: cancellationToken);

    internal static Task<IResult> CancelTaskAsync(IA2ARequestHandler requestHandler, ILogger logger, string id, CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(logger, "CancelTask", async ct =>
        {
            var cancelledTask = await requestHandler.CancelTaskAsync(new CancelTaskRequest { Id = id }, ct).ConfigureAwait(false);
            return new JsonRpcResponseResult(JsonRpcResponse.CreateJsonRpcResponse(new JsonRpcId("http"), cancelledTask));
        }, id, cancellationToken: cancellationToken);

    internal static Task<IResult> SendMessageAsync(IA2ARequestHandler requestHandler, ILogger logger, SendMessageRequest sendRequest, CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(logger, "SendMessage", async ct =>
        {
            var result = await requestHandler.SendMessageAsync(sendRequest, ct).ConfigureAwait(false);
            return new JsonRpcResponseResult(JsonRpcResponse.CreateJsonRpcResponse(new JsonRpcId("http"), result));
        }, cancellationToken: cancellationToken);

    internal static IResult SendMessageStream(IA2ARequestHandler requestHandler, ILogger logger, SendMessageRequest sendRequest, CancellationToken cancellationToken)
        => WithExceptionHandling(logger, nameof(SendMessageStream), () =>
        {
            var events = requestHandler.SendStreamingMessageAsync(sendRequest, cancellationToken);
            return new JsonRpcStreamedResult(events, new JsonRpcId("http"));
        });

    internal static IResult SubscribeToTask(IA2ARequestHandler requestHandler, ILogger logger, string id, CancellationToken cancellationToken)
        => WithExceptionHandling(logger, nameof(SubscribeToTask), () =>
        {
            var events = requestHandler.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = id }, cancellationToken);
            return new JsonRpcStreamedResult(events, new JsonRpcId("http"));
        }, id);

    private static async Task<IResult> WithExceptionHandlingAsync(ILogger logger, string activityName,
        Func<CancellationToken, Task<IResult>> operation, string? taskId = null, CancellationToken cancellationToken = default)
    {
        using var activity = A2AAspNetCoreDiagnostics.Source.StartActivity(activityName, ActivityKind.Server);
        if (taskId is not null)
        {
            activity?.SetTag("task.id", taskId);
        }

        try
        {
            return await operation(cancellationToken);
        }
        catch (A2AException ex)
        {
            logger.A2AErrorInActivityName(ex, activityName);
            return MapA2AExceptionToHttpResult(ex);
        }
        catch (Exception ex)
        {
            logger.UnexpectedErrorInActivityName(ex, activityName);
            return new A2AErrorResult(new A2AException("An internal error occurred.", A2AErrorCode.InternalError));
        }
    }

    private static IResult WithExceptionHandling(ILogger logger, string activityName,
        Func<IResult> operation, string? taskId = null)
    {
        using var activity = A2AAspNetCoreDiagnostics.Source.StartActivity(activityName, ActivityKind.Server);
        if (taskId is not null)
        {
            activity?.SetTag("task.id", taskId);
        }

        try
        {
            return operation();
        }
        catch (A2AException ex)
        {
            logger.A2AErrorInActivityName(ex, activityName);
            return MapA2AExceptionToHttpResult(ex);
        }
        catch (Exception ex)
        {
            logger.UnexpectedErrorInActivityName(ex, activityName);
            return new A2AErrorResult(new A2AException("An internal error occurred.", A2AErrorCode.InternalError));
        }
    }

    private static A2AErrorResult MapA2AExceptionToHttpResult(A2AException exception) =>
        new A2AErrorResult(exception);
}

/// <summary>IResult for REST API Server-Sent Events streaming.</summary>
internal sealed class A2AEventStreamResult : IResult
{
    private readonly A2AEventStreamResult<StreamResponse> _inner;

    internal A2AEventStreamResult(IAsyncEnumerable<StreamResponse> events)
    {
        _inner = new A2AEventStreamResult<StreamResponse>(
            events,
            (JsonTypeInfo<StreamResponse>)A2AJsonUtilities.DefaultOptions
                .GetTypeInfo(typeof(StreamResponse)),
            static exception =>
            {
                var error = exception is A2AException a2aException
                    ? a2aException
                    : new A2AException(
                        "An internal error occurred.",
                        A2AErrorCode.InternalError);
                return new A2AErrorResult(error);
            },
            operationActivity: null);
    }

    public Task ExecuteAsync(HttpContext httpContext) =>
        _inner.ExecuteAsync(httpContext);
}

internal sealed class A2AEventStreamResult<TEvent>(
    IAsyncEnumerable<TEvent> events,
    JsonTypeInfo<TEvent> eventTypeInfo,
    Func<Exception, IResult> createErrorResult,
    Activity? operationActivity,
    Action<Exception>? logStreamException = null)
    : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        IAsyncEnumerator<TEvent> enumerator;
        try
        {
            enumerator = events.GetAsyncEnumerator(httpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            A2AOperationDiagnostics.SetOutcome(operationActivity, "cancelled");
            return;
        }
        catch (Exception exception)
        {
            A2AOperationDiagnostics.SetError(operationActivity, exception);
            await WriteErrorAsync(
                httpContext,
                exception,
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
                do
                {
#pragma warning disable VSTHRD103 // Serialize to string is not blocking I/O
                    var json = JsonSerializer.Serialize(
                        enumerator.Current,
                        eventTypeInfo);
#pragma warning restore VSTHRD103
                    var frame = Encoding.UTF8.GetBytes($"data: {json}\n\n");
                    if (!streamStarted)
                    {
                        ConfigureSseResponse(httpContext);
                    }

                    await httpContext.Response.BodyWriter.WriteAsync(
                        frame,
                        httpContext.RequestAborted);
                    streamStarted = true;
                    await httpContext.Response.BodyWriter.FlushAsync(httpContext.RequestAborted);
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
            cancelled = true;
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
                cancelled = true;
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }

        if (failure is not null)
        {
            A2AOperationDiagnostics.SetError(operationActivity, failure);
            await WriteErrorAsync(httpContext, failure, streamStarted).ConfigureAwait(false);
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
        if (!streamStarted)
        {
            IResult errorResult;
            try
            {
                errorResult = createErrorResult(exception);
            }
            catch (Exception mappingException)
            {
                Activity.Current?.AddException(mappingException);
                errorResult = new A2AErrorResult(
                    new A2AException(
                        "An internal error occurred.",
                        A2AErrorCode.InternalError));
            }

            await errorResult.ExecuteAsync(httpContext).ConfigureAwait(false);
            return;
        }

        logStreamException?.Invoke(exception);
        try
        {
            await httpContext.Response.BodyWriter.WriteAsync(
                Encoding.UTF8.GetBytes("data: {\"error\":\"An internal error occurred during streaming.\"}\n\n"),
                httpContext.RequestAborted);
            await httpContext.Response.BodyWriter.FlushAsync(httpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }
}

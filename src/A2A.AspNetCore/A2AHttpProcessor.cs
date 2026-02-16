using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace A2A.AspNetCore;

/// <summary>
/// Static processor class for handling A2A HTTP requests in ASP.NET Core applications.
/// </summary>
internal static class A2AHttpProcessor
{
    /// <summary>Activity source for tracing A2A HTTP operations.</summary>
    public static readonly ActivitySource ActivitySource = new("A2A.HttpProcessor", "1.0.0");

    internal static Task<IResult> GetTaskAsync(ITaskManager taskManager, ILogger logger, string id, int? historyLength, string? metadata, CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(logger, "GetTask", async ct =>
        {
            var agentTask = await taskManager.GetTaskAsync(new GetTaskRequest
            {
                Id = id,
                HistoryLength = historyLength,
            }, ct).ConfigureAwait(false);

            return new JsonRpcResponseResult(JsonRpcResponse.CreateJsonRpcResponse(new JsonRpcId("http"), agentTask));
        }, id, cancellationToken: cancellationToken);

    internal static Task<IResult> CancelTaskAsync(ITaskManager taskManager, ILogger logger, string id, CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(logger, "CancelTask", async ct =>
        {
            var cancelledTask = await taskManager.CancelTaskAsync(new CancelTaskRequest { Id = id }, ct).ConfigureAwait(false);
            return new JsonRpcResponseResult(JsonRpcResponse.CreateJsonRpcResponse(new JsonRpcId("http"), cancelledTask));
        }, id, cancellationToken: cancellationToken);

    internal static Task<IResult> SendMessageAsync(ITaskManager taskManager, ILogger logger, SendMessageRequest sendRequest, CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(logger, "SendMessage", async ct =>
        {
            var result = await taskManager.SendMessageAsync(sendRequest, ct).ConfigureAwait(false);
            return new JsonRpcResponseResult(JsonRpcResponse.CreateJsonRpcResponse(new JsonRpcId("http"), result));
        }, cancellationToken: cancellationToken);

    internal static IResult SendMessageStream(ITaskManager taskManager, ILogger logger, SendMessageRequest sendRequest, CancellationToken cancellationToken)
        => WithExceptionHandling(logger, nameof(SendMessageStream), () =>
        {
            var events = taskManager.SendStreamingMessageAsync(sendRequest, cancellationToken);
            return new JsonRpcStreamedResult(events, new JsonRpcId("http"));
        });

    internal static IResult SubscribeToTask(ITaskManager taskManager, ILogger logger, string id, CancellationToken cancellationToken)
        => WithExceptionHandling(logger, nameof(SubscribeToTask), () =>
        {
            var events = taskManager.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = id }, cancellationToken);
            return new JsonRpcStreamedResult(events, new JsonRpcId("http"));
        }, id);

    private static async Task<IResult> WithExceptionHandlingAsync(ILogger logger, string activityName,
        Func<CancellationToken, Task<IResult>> operation, string? taskId = null, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity(activityName, ActivityKind.Server);
        if (taskId is not null)
        {
            activity?.AddTag("task.id", taskId);
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
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult WithExceptionHandling(ILogger logger, string activityName,
        Func<IResult> operation, string? taskId = null)
    {
        using var activity = ActivitySource.StartActivity(activityName, ActivityKind.Server);
        if (taskId is not null)
        {
            activity?.AddTag("task.id", taskId);
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
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult MapA2AExceptionToHttpResult(A2AException exception)
    {
        return exception.ErrorCode switch
        {
            A2AErrorCode.TaskNotFound or
            A2AErrorCode.MethodNotFound => Results.NotFound(exception.Message),

            A2AErrorCode.TaskNotCancelable or
            A2AErrorCode.UnsupportedOperation or
            A2AErrorCode.InvalidRequest or
            A2AErrorCode.InvalidParams or
            A2AErrorCode.ParseError => Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest),

            A2AErrorCode.PushNotificationNotSupported => Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest),

            A2AErrorCode.ContentTypeNotSupported => Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status422UnprocessableEntity),

            A2AErrorCode.InternalError => Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status500InternalServerError),

            _ => Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    // ======= REST API exception handling (simplified, without activity tracing) =======

    private static async Task<IResult> WithExceptionHandlingAsync(
        Func<CancellationToken, Task<IResult>> operation, CancellationToken cancellationToken)
    {
        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        catch (A2AException ex)
        {
            return MapA2AExceptionToHttpResult(ex);
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult WithExceptionHandling(Func<IResult> operation)
    {
        try
        {
            return operation();
        }
        catch (A2AException ex)
        {
            return MapA2AExceptionToHttpResult(ex);
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ======= REST API handler methods =======

    // REST handler: Get agent card
    internal static Task<IResult> GetAgentCardRestAsync(
        ITaskManager taskManager, AgentCard agentCard, CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(ct =>
            Task.FromResult<IResult>(new A2AResponseResult(agentCard)), cancellationToken);

    // REST handler: Get task by ID
    internal static Task<IResult> GetTaskRestAsync(
        ITaskManager taskManager, string id, int? historyLength, CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(async ct =>
        {
            var result = await taskManager.GetTaskAsync(
                new GetTaskRequest { Id = id, HistoryLength = historyLength }, ct).ConfigureAwait(false);
            return new A2AResponseResult(result);
        }, cancellationToken);

    // REST handler: Cancel task
    internal static Task<IResult> CancelTaskRestAsync(
        ITaskManager taskManager, string id, CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(async ct =>
        {
            var result = await taskManager.CancelTaskAsync(
                new CancelTaskRequest { Id = id }, ct).ConfigureAwait(false);
            return new A2AResponseResult(result);
        }, cancellationToken);

    // REST handler: Send message
    internal static Task<IResult> SendMessageRestAsync(
        ITaskManager taskManager, SendMessageRequest request, CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(async ct =>
        {
            var result = await taskManager.SendMessageAsync(request, ct).ConfigureAwait(false);
            return new A2AResponseResult(result);
        }, cancellationToken);

    // REST handler: Send streaming message
    internal static IResult SendMessageStreamRest(
        ITaskManager taskManager, SendMessageRequest request, CancellationToken cancellationToken)
        => WithExceptionHandling(() =>
        {
            var events = taskManager.SendStreamingMessageAsync(request, cancellationToken);
            return new A2AEventStreamResult(events);
        });

    // REST handler: Subscribe to task
    internal static IResult SubscribeToTaskRest(
        ITaskManager taskManager, string id, CancellationToken cancellationToken)
        => WithExceptionHandling(() =>
        {
            var events = taskManager.SubscribeToTaskAsync(
                new SubscribeToTaskRequest { Id = id }, cancellationToken);
            return new A2AEventStreamResult(events);
        });

    // REST handler: List tasks
    internal static Task<IResult> ListTasksRestAsync(
        ITaskManager taskManager, string? contextId, string? status, int? pageSize,
        string? pageToken, int? historyLength, CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(async ct =>
        {
            var request = new ListTasksRequest
            {
                ContextId = contextId,
                PageSize = pageSize,
                PageToken = pageToken,
                HistoryLength = historyLength,
            };
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<TaskState>(status, ignoreCase: true, out var taskState))
            {
                request.Status = taskState;
            }

            var result = await taskManager.ListTasksAsync(request, ct).ConfigureAwait(false);
            return new A2AResponseResult(result);
        }, cancellationToken);

    // REST handler: Get extended agent card
    internal static Task<IResult> GetExtendedAgentCardRestAsync(
        ITaskManager taskManager, CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(async ct =>
        {
            var result = await taskManager.GetExtendedAgentCardAsync(
                new GetExtendedAgentCardRequest(), ct).ConfigureAwait(false);
            return new A2AResponseResult(result);
        }, cancellationToken);

    // REST handler: Create push notification config
    internal static Task<IResult> CreatePushNotificationConfigRestAsync(
        ITaskManager taskManager, string taskId, PushNotificationConfig config, CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(async ct =>
        {
            var request = new CreateTaskPushNotificationConfigRequest
            {
                TaskId = taskId,
                Config = config,
                ConfigId = config.Id ?? string.Empty,
            };
            var result = await taskManager.CreateTaskPushNotificationConfigAsync(request, ct).ConfigureAwait(false);
            return new A2AResponseResult(result);
        }, cancellationToken);

    // REST handler: List push notification configs for a task
    internal static Task<IResult> ListPushNotificationConfigRestAsync(
        ITaskManager taskManager, string taskId, int? pageSize, string? pageToken,
        CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(async ct =>
        {
            var request = new ListTaskPushNotificationConfigRequest
            {
                TaskId = taskId,
                PageSize = pageSize,
                PageToken = pageToken,
            };
            var result = await taskManager.ListTaskPushNotificationConfigAsync(request, ct)
                .ConfigureAwait(false);
            return new A2AResponseResult(result);
        }, cancellationToken);

    // REST handler: Get push notification config
    internal static Task<IResult> GetPushNotificationConfigRestAsync(
        ITaskManager taskManager, string taskId, string configId, CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(async ct =>
        {
            var request = new GetTaskPushNotificationConfigRequest { TaskId = taskId, Id = configId };
            var result = await taskManager.GetTaskPushNotificationConfigAsync(request, ct).ConfigureAwait(false);
            return new A2AResponseResult(result);
        }, cancellationToken);

    // REST handler: Delete push notification config
    internal static Task<IResult> DeletePushNotificationConfigRestAsync(
        ITaskManager taskManager, string taskId, string configId, CancellationToken cancellationToken)
        => WithExceptionHandlingAsync(async ct =>
        {
            var request = new DeleteTaskPushNotificationConfigRequest { TaskId = taskId, Id = configId };
            await taskManager.DeleteTaskPushNotificationConfigAsync(request, ct).ConfigureAwait(false);
            return Results.NoContent();
        }, cancellationToken);
}

/// <summary>IResult for REST API JSON responses.</summary>
internal sealed class A2AResponseResult : IResult
{
    private readonly object _response;
    private readonly Type _responseType;

    internal A2AResponseResult(SendMessageResponse response) { _response = response; _responseType = typeof(SendMessageResponse); }
    internal A2AResponseResult(AgentTask task) { _response = task; _responseType = typeof(AgentTask); }
    internal A2AResponseResult(ListTasksResponse response) { _response = response; _responseType = typeof(ListTasksResponse); }
    internal A2AResponseResult(AgentCard card) { _response = card; _responseType = typeof(AgentCard); }
    internal A2AResponseResult(TaskPushNotificationConfig config) { _response = config; _responseType = typeof(TaskPushNotificationConfig); }
    internal A2AResponseResult(ListTaskPushNotificationConfigResponse response) { _response = response; _responseType = typeof(ListTaskPushNotificationConfigResponse); }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, _response,
            A2AJsonUtilities.DefaultOptions.GetTypeInfo(_responseType));
    }
}

/// <summary>IResult for REST API Server-Sent Events streaming.</summary>
internal sealed class A2AEventStreamResult : IResult
{
    private readonly IAsyncEnumerable<StreamResponse> _events;

    internal A2AEventStreamResult(IAsyncEnumerable<StreamResponse> events) => _events = events;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache,no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
        httpContext.Response.Headers.ContentEncoding = "identity";

        var bufferingFeature = httpContext.Features.GetRequiredFeature<IHttpResponseBodyFeature>();
        bufferingFeature.DisableBuffering();

        try
        {
            await foreach (var taskEvent in _events.WithCancellation(httpContext.RequestAborted))
            {
                var json = JsonSerializer.Serialize(taskEvent,
                    A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(StreamResponse)));
                await httpContext.Response.BodyWriter.WriteAsync(
                    Encoding.UTF8.GetBytes($"data: {json}\n\n"), httpContext.RequestAborted);
                await httpContext.Response.BodyWriter.FlushAsync(httpContext.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — expected
        }
    }
}
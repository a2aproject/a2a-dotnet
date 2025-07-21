using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace A2A.AspNetCore;

/// <summary>
/// Static processor class for handling A2A HTTP requests in ASP.NET Core applications.
/// </summary>
/// <remarks>
/// Provides methods for processing agent card queries, task operations, message sending,
/// and push notification configuration through HTTP endpoints.
/// </remarks>
internal static class A2AHttpProcessor
{
    /// <summary>
    /// OpenTelemetry ActivitySource for tracing HTTP processor operations.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("A2A.HttpProcessor", "1.0.0");

    /// <summary>
    /// Processes a request to retrieve the agent card containing agent capabilities and metadata.
    /// </summary>
    /// <remarks>
    /// Invokes the task manager's agent card query handler to get current agent information.
    /// </remarks>
    /// <param name="taskManager">The task manager instance containing the agent card query handler.</param>
    /// <param name="logger">Logger instance for recording operation details and errors.</param>
    /// <param name="agentUrl">The URL of the agent to retrieve the card for.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An HTTP result containing the agent card JSON or an error response.</returns>
    internal static Task<IResult> GetAgentCardAsync(ITaskManager taskManager, ILogger logger, string agentUrl, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("GetAgentCard", ActivityKind.Server);

        try
        {
            var agentCard = taskManager.OnAgentCardQuery(agentUrl, cancellationToken);

            return Task.FromResult(Results.Ok(agentCard));
        }
        catch (A2AException ex)
        {
            logger.LogError(ex, "A2A error retrieving agent card");
            return Task.FromResult(MapA2AExceptionToHttpResult(ex));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving agent card");
            return Task.FromResult(Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError));
        }
    }

    /// <summary>
    /// Processes a request to retrieve a specific task by its ID.
    /// </summary>
    /// <remarks>
    /// Returns the task's current state, history, and metadata with optional history length limiting.
    /// </remarks>
    /// <param name="taskManager">The task manager instance for accessing task storage.</param>
    /// <param name="logger">Logger instance for recording operation details and errors.</param>
    /// <param name="id">The unique identifier of the task to retrieve.</param>
    /// <param name="historyLength">Optional limit on the number of history items to return.</param>
    /// <param name="metadata">Optional JSON metadata filter for the task query.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An HTTP result containing the task JSON or a not found/error response.</returns>
    internal static async Task<IResult> GetTaskAsync(ITaskManager taskManager, ILogger logger, string id, int? historyLength, string? metadata, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("GetTask", ActivityKind.Server);
        activity?.AddTag("task.id", id);

        try
        {
            var agentTask = await taskManager.GetTaskAsync(new TaskQueryParams()
            {
                Id = id,
                HistoryLength = historyLength,
                Metadata = string.IsNullOrWhiteSpace(metadata) ? null : (Dictionary<string, JsonElement>?)JsonSerializer.Deserialize(metadata, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(Dictionary<string, JsonElement>)))
            }, cancellationToken);

            return agentTask is not null ? new A2AResponseResult(agentTask) : Results.NotFound();
        }
        catch (A2AException ex)
        {
            logger.LogError(ex, "A2A error retrieving task");
            return MapA2AExceptionToHttpResult(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving task");
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Processes a request to cancel a specific task by setting its status to Canceled.
    /// </summary>
    /// <remarks>
    /// Invokes the task manager's cancellation logic and returns the updated task state.
    /// </remarks>
    /// <param name="taskManager">The task manager instance for handling task cancellation.</param>
    /// <param name="logger">Logger instance for recording operation details and errors.</param>
    /// <param name="id">The unique identifier of the task to cancel.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An HTTP result containing the canceled task JSON or a not found/error response.</returns>
    internal static async Task<IResult> CancelTaskAsync(ITaskManager taskManager, ILogger logger, string id, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("CancelTask", ActivityKind.Server);
        activity?.AddTag("task.id", id);

        try
        {
            var agentTask = await taskManager.CancelTaskAsync(new TaskIdParams { Id = id }, cancellationToken);
            if (agentTask == null)
            {
                return Results.NotFound();
            }

            return new A2AResponseResult(agentTask);
        }
        catch (A2AException ex)
        {
            logger.LogError(ex, "A2A error cancelling task");
            return MapA2AExceptionToHttpResult(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cancelling task");
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Processes a request to send a message to a task and return a single response.
    /// </summary>
    /// <remarks>
    /// Creates a new task if no task ID is provided, or updates an existing task's history.
    /// Configures message sending parameters including history length and metadata.
    /// </remarks>
    /// <param name="taskManager">The task manager instance for handling message processing.</param>
    /// <param name="logger">Logger instance for recording operation details and errors.</param>
    /// <param name="sendParams">The message parameters containing the message content and configuration.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An HTTP result containing the agent's response (Task or Message) or an error response.</returns>
    internal static async Task<IResult> SendMessageAsync(ITaskManager taskManager, ILogger logger, MessageSendParams sendParams, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("SendMessage", ActivityKind.Server);
        try
        {
            var a2aResponse = await taskManager.SendMessageAsync(sendParams, cancellationToken);
            if (a2aResponse == null)
            {
                return Results.NotFound();
            }

            return new A2AResponseResult(a2aResponse);
        }
        catch (A2AException ex)
        {
            logger.LogError(ex, "A2A error sending message to task");
            return MapA2AExceptionToHttpResult(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending message to task");
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Processes a request to send a message to a task and return a stream of events.
    /// </summary>
    /// <remarks>
    /// Creates or updates a task and establishes a Server-Sent Events stream that yields
    /// Task, Message, TaskStatusUpdateEvent, and TaskArtifactUpdateEvent objects as they occur.
    /// </remarks>
    /// <param name="taskManager">The task manager instance for handling streaming message processing.</param>
    /// <param name="logger">Logger instance for recording operation details and errors.</param>
    /// <param name="sendParams">The message parameters containing the message content and configuration.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An HTTP result that streams events as Server-Sent Events or an error response.</returns>
    internal static async Task<IResult> SendMessageStreamAsync(ITaskManager taskManager, ILogger logger, MessageSendParams sendParams, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("SendMessageStream", ActivityKind.Server);
        activity?.AddTag("task.id", sendParams.Message.TaskId);

        try
        {
            var taskEvents = await taskManager.SendMessageStreamAsync(sendParams, cancellationToken);

            return new A2AEventStreamResult(taskEvents);
        }
        catch (A2AException ex)
        {
            logger.LogError(ex, "A2A error sending subscribe message to task");
            return MapA2AExceptionToHttpResult(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending subscribe message to task");
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Processes a request to resubscribe to an existing task's event stream.
    /// </summary>
    /// <remarks>
    /// Returns the active event enumerator for the specified task, allowing clients
    /// to reconnect to ongoing task updates via Server-Sent Events.
    /// </remarks>
    /// <param name="taskManager">The task manager instance containing active task event streams.</param>
    /// <param name="logger">Logger instance for recording operation details and errors.</param>
    /// <param name="id">The unique identifier of the task to resubscribe to.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An HTTP result that streams existing task events or an error response.</returns>
    internal static IResult SubscribeTask(ITaskManager taskManager, ILogger logger, string id, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("SubscribeTask", ActivityKind.Server);
        activity?.AddTag("task.id", id);

        try
        {
            var taskEvents = taskManager.SubscribeToTaskAsync(new TaskIdParams { Id = id }, cancellationToken);

            return new A2AEventStreamResult(taskEvents);
        }
        catch (A2AException ex)
        {
            logger.LogError(ex, "A2A error resubscribing to task");
            return MapA2AExceptionToHttpResult(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error subscribing to task");
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Processes a request to set or update push notification configuration for a specific task.
    /// </summary>
    /// <remarks>
    /// Configures callback URLs and authentication settings for receiving task update notifications via HTTP.
    /// </remarks>
    /// <param name="taskManager">The task manager instance for handling push notification configuration.</param>
    /// <param name="logger">Logger instance for recording operation details and errors.</param>
    /// <param name="id">The unique identifier of the task to configure push notifications for.</param>
    /// <param name="pushNotificationConfig">The push notification configuration containing callback URL and authentication details.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An HTTP result containing the configured settings or an error response.</returns>
    internal static async Task<IResult> SetPushNotificationAsync(ITaskManager taskManager, ILogger logger, string id, PushNotificationConfig pushNotificationConfig, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("ConfigurePushNotification", ActivityKind.Server);
        activity?.AddTag("task.id", id);

        try
        {
            var taskIdParams = new TaskIdParams { Id = id };
            var result = await taskManager.SetPushNotificationAsync(new TaskPushNotificationConfig
            {
                TaskId = id,
                PushNotificationConfig = pushNotificationConfig
            }, cancellationToken);

            if (result == null)
            {
                return Results.NotFound();
            }

            return Results.Ok(result);
        }
        catch (A2AException ex)
        {
            logger.LogError(ex, "A2A error configuring push notification");
            return MapA2AExceptionToHttpResult(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error configuring push notification");
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Processes a request to retrieve the push notification configuration for a specific task.
    /// </summary>
    /// <remarks>
    /// Returns the callback URL and authentication settings configured for receiving task update notifications.
    /// </remarks>
    /// <param name="taskManager">The task manager instance for accessing push notification configurations.</param>
    /// <param name="logger">Logger instance for recording operation details and errors.</param>
    /// <param name="taskId">The unique identifier of the task to get push notification configuration for.</param>
    /// <param name="notificationConfigId">The unique identifier of the push notification configuration to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An HTTP result containing the push notification configuration or a not found/error response.</returns>
    internal static async Task<IResult> GetPushNotificationAsync(ITaskManager taskManager, ILogger logger, string taskId, string? notificationConfigId, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("GetPushNotification", ActivityKind.Server);
        activity?.AddTag("task.id", taskId);

        try
        {
            var taskIdParams = new GetTaskPushNotificationConfigParams { Id = taskId, PushNotificationConfigId = notificationConfigId };
            var result = await taskManager.GetPushNotificationAsync(taskIdParams, cancellationToken);

            if (result == null)
            {
                return Results.NotFound();
            }

            return Results.Ok(result);
        }
        catch (A2AException ex)
        {
            logger.LogError(ex, "A2A error retrieving push notification");
            return MapA2AExceptionToHttpResult(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving push notification");
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Maps an A2AException to an appropriate HTTP result based on the error code.
    /// </summary>
    /// <param name="exception">The A2AException to map to an HTTP result.</param>
    /// <returns>An HTTP result with the appropriate status code and error message.</returns>
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

            // Return HTTP 400 for now. Later we may want to return 501 Not Implemented in case 
            // push notifications are advertised by agent card(AgentCard.capabilities.pushNotifications: true)
            // but there's no server-side support for them.
            A2AErrorCode.PushNotificationNotSupported => Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest),

            A2AErrorCode.ContentTypeNotSupported => Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status422UnprocessableEntity),

            A2AErrorCode.InternalError => Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status500InternalServerError),

            // Default case for unhandled error codes - this should never happen with current A2AErrorCode enum values
            // but provides a safety net for future enum additions or unexpected values
            _ => Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}

/// <summary>
/// Result type for returning A2A responses as JSON in HTTP responses.
/// </summary>
/// <remarks>
/// Implements IResult to provide custom serialization of A2AResponse objects
/// using the configured JSON serialization options.
/// </remarks>
public class A2AResponseResult : IResult
{
    private readonly A2AResponse a2aResponse;

    /// <summary>
    /// Initializes a new instance of the A2AResponseResult class.
    /// </summary>
    /// <param name="a2aResponse">The A2A response object to serialize and return in the HTTP response.</param>
    public A2AResponseResult(A2AResponse a2aResponse)
    {
        this.a2aResponse = a2aResponse;
    }

    /// <summary>
    /// Executes the result by serializing the A2A response as JSON to the HTTP response body.
    /// </summary>
    /// <remarks>
    /// Sets the appropriate content type and uses the default A2A JSON serialization options.
    /// </remarks>
    /// <param name="httpContext">The HTTP context to write the response to.</param>
    /// <returns>A task representing the asynchronous serialization operation.</returns>
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.ContentType = "application/json";

        await JsonSerializer.SerializeAsync(httpContext.Response.Body, a2aResponse, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(A2AResponse)));
    }
}

/// <summary>
/// Result type for streaming A2A events as Server-Sent Events (SSE) in HTTP responses.
/// </summary>
/// <remarks>
/// Implements IResult to provide real-time streaming of task events including Task objects,
/// TaskStatusUpdateEvent, and TaskArtifactUpdateEvent objects.
/// </remarks>
public class A2AEventStreamResult : IResult
{
    private readonly IAsyncEnumerable<A2AEvent> taskEvents;

    /// <summary>
    /// Initializes a new instance of the A2AEventStreamResult class.
    /// </summary>
    /// <param name="taskEvents">The async enumerable stream of A2A events to send as Server-Sent Events.</param>
    public A2AEventStreamResult(IAsyncEnumerable<A2AEvent> taskEvents)
    {
        ArgumentNullException.ThrowIfNull(taskEvents);

        this.taskEvents = taskEvents;
    }

    /// <summary>
    /// Executes the result by streaming A2A events as Server-Sent Events to the HTTP response.
    /// </summary>
    /// <remarks>
    /// Sets the appropriate SSE content type and formats each event according to the SSE specification.
    /// Each event is serialized as JSON and sent with the "data:" prefix followed by double newlines.
    /// </remarks>
    /// <param name="httpContext">The HTTP context to stream the events to.</param>
    /// <returns>A task representing the asynchronous streaming operation.</returns>
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.ContentType = "text/event-stream";
        await foreach (var taskEvent in taskEvents)
        {
            var json = JsonSerializer.Serialize(taskEvent, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(A2AEvent)));
            await httpContext.Response.BodyWriter.WriteAsync(Encoding.UTF8.GetBytes($"data: {json}\n\n"));
            await httpContext.Response.BodyWriter.FlushAsync();
        }
    }
}
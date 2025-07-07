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
public static class A2AHttpProcessor
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
    /// <returns>An HTTP result containing the agent card JSON or an error response</returns>
    internal static Task<IResult> GetAgentCard(TaskManager taskManager, ILogger logger, string agentUrl)
    {
        using var activity = ActivitySource.StartActivity("GetAgentCard", ActivityKind.Server);

        try
        {
            var agentCard = taskManager.OnAgentCardQuery(agentUrl);

            return Task.FromResult(Results.Ok(agentCard));
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
    /// <returns>An HTTP result containing the task JSON or a not found/error response</returns>
    internal static async Task<IResult> GetTask(TaskManager taskManager, ILogger logger, string id, int? historyLength, string? metadata)
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
            });

            return agentTask is not null ? new A2AResponseResult(agentTask) : Results.NotFound();
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
    /// <returns>An HTTP result containing the canceled task JSON or a not found/error response</returns>
    internal static async Task<IResult> CancelTask(TaskManager taskManager, ILogger logger, string id)
    {
        using var activity = ActivitySource.StartActivity("CancelTask", ActivityKind.Server);
        activity?.AddTag("task.id", id);

        try
        {
            var agentTask = await taskManager.CancelTaskAsync(new TaskIdParams { Id = id });
            if (agentTask == null)
            {
                return Results.NotFound();
            }

            return new A2AResponseResult(agentTask);
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
    /// <param name="taskId">Optional task ID to send the message to. If null, a new task may be created.</param>
    /// <param name="sendParams">The message parameters containing the message content and configuration.</param>
    /// <param name="historyLength">Optional limit on the number of history items to include in processing.</param>
    /// <param name="metadata">Optional JSON metadata to include with the message request.</param>
    /// <returns>An HTTP result containing the agent's response (Task or Message) or an error response</returns>
    internal static async Task<IResult> SendTaskMessage(TaskManager taskManager, ILogger logger, string? taskId, MessageSendParams sendParams, int? historyLength, string? metadata)
    {
        using var activity = ActivitySource.StartActivity("SendTaskMessage", ActivityKind.Server);
        if (taskId != null)
        {
            activity?.AddTag("task.id", taskId);
        }

        try
        {
            if (taskId != null)
            {
                sendParams.Message.TaskId = taskId;
            }
            sendParams.Configuration = new MessageSendConfiguration
            {
                HistoryLength = historyLength
            };
            sendParams.Metadata = string.IsNullOrWhiteSpace(metadata) ? null : (Dictionary<string, JsonElement>?)JsonSerializer.Deserialize(metadata, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(Dictionary<string, JsonElement>)));

            var a2aResponse = await taskManager.SendMessageAsync(sendParams);
            if (a2aResponse == null)
            {
                return Results.NotFound();
            }

            return new A2AResponseResult(a2aResponse);
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
    /// <param name="id">The unique identifier of the task to send the message to.</param>
    /// <param name="sendParams">The message parameters containing the message content and configuration.</param>
    /// <param name="historyLength">Optional limit on the number of history items to include in processing.</param>
    /// <param name="metadata">Optional JSON metadata to include with the message request.</param>
    /// <returns>An HTTP result that streams events as Server-Sent Events or an error response</returns>
    internal static async Task<IResult> SendSubscribeTaskMessage(TaskManager taskManager, ILogger logger, string id, MessageSendParams sendParams, int? historyLength, string? metadata)
    {
        using var activity = ActivitySource.StartActivity("SendSubscribeTaskMessage", ActivityKind.Server);
        activity?.AddTag("task.id", id);

        try
        {
            sendParams.Message.TaskId = id;
            sendParams.Configuration = new MessageSendConfiguration()
            {
                HistoryLength = historyLength
            };
            sendParams.Metadata = string.IsNullOrWhiteSpace(metadata) ? null : (Dictionary<string, JsonElement>?)JsonSerializer.Deserialize(metadata, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(Dictionary<string, JsonElement>)));

            var taskEvents = await taskManager.SendMessageStreamAsync(sendParams);

            return new A2AEventStreamResult(taskEvents);
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
    /// <param name="taskManager">The task manager instance containing active task event streams</param>
    /// <param name="logger">Logger instance for recording operation details and errors</param>
    /// <param name="id">The unique identifier of the task to resubscribe to</param>
    /// <returns>An HTTP result that streams existing task events or an error response</returns>
    internal static IResult ResubscribeTask(TaskManager taskManager, ILogger logger, string id)
    {
        using var activity = ActivitySource.StartActivity("ResubscribeTask", ActivityKind.Server);
        activity?.AddTag("task.id", id);

        try
        {
            var taskEvents = taskManager.ResubscribeAsync(new TaskIdParams { Id = id });

            return new A2AEventStreamResult(taskEvents);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resubscribing to task");
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Processes a request to set or update push notification configuration for a specific task.
    /// </summary>
    /// <remarks>
    /// Configures callback URLs and authentication settings for receiving task update notifications via HTTP.
    /// </remarks>
    /// <param name="taskManager">The task manager instance for handling push notification configuration</param>
    /// <param name="logger">Logger instance for recording operation details and errors</param>
    /// <param name="id">The unique identifier of the task to configure push notifications for</param>
    /// <param name="pushNotificationConfig">The push notification configuration containing callback URL and authentication details</param>
    /// <returns>An HTTP result containing the configured settings or an error response</returns>
    internal static async Task<IResult> SetPushNotification(TaskManager taskManager, ILogger logger, string id, PushNotificationConfig pushNotificationConfig)
    {
        using var activity = ActivitySource.StartActivity("ConfigurePushNotification", ActivityKind.Server);
        activity?.AddTag("task.id", id);

        try
        {
            var taskIdParams = new TaskIdParams { Id = id };
            var result = await taskManager.SetPushNotificationAsync(new TaskPushNotificationConfig
            {
                Id = id,
                PushNotificationConfig = pushNotificationConfig
            });

            if (result == null)
            {
                return Results.NotFound();
            }

            return Results.Ok(result);
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
    /// <param name="taskManager">The task manager instance for accessing push notification configurations</param>
    /// <param name="logger">Logger instance for recording operation details and errors</param>
    /// <param name="id">The unique identifier of the task to get push notification configuration for</param>
    /// <returns>An HTTP result containing the push notification configuration or a not found/error response</returns>
    internal static async Task<IResult> GetPushNotification(TaskManager taskManager, ILogger logger, string id)
    {
        using var activity = ActivitySource.StartActivity("GetPushNotification", ActivityKind.Server);
        activity?.AddTag("task.id", id);

        try
        {
            var taskIdParams = new TaskIdParams { Id = id };
            var result = await taskManager.GetPushNotificationAsync(taskIdParams);

            if (result == null)
            {
                return Results.NotFound();
            }

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving push notification");
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
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
    /// <param name="a2aResponse">The A2A response object to serialize and return in the HTTP response</param>
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
    /// <param name="httpContext">The HTTP context to write the response to</param>
    /// <returns>A task representing the asynchronous serialization operation</returns>
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
    /// <param name="taskEvents">The async enumerable stream of A2A events to send as Server-Sent Events</param>
    public A2AEventStreamResult(IAsyncEnumerable<A2AEvent> taskEvents)
    {
        this.taskEvents = taskEvents;
    }

    /// <summary>
    /// Executes the result by streaming A2A events as Server-Sent Events to the HTTP response.
    /// </summary>
    /// <remarks>
    /// Sets the appropriate SSE content type and formats each event according to the SSE specification.
    /// Each event is serialized as JSON and sent with the "data:" prefix followed by double newlines.
    /// </remarks>
    /// <param name="httpContext">The HTTP context to stream the events to</param>
    /// <returns>A task representing the asynchronous streaming operation</returns>
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.ContentType = "text/event-stream";
        await foreach (var taskEvent in taskEvents)
        {
            var json = JsonSerializer.Serialize(taskEvent, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(A2AEvent)));
            await httpContext.Response.BodyWriter.WriteAsync(Encoding.UTF8.GetBytes($"data: {json}\n\n"));
            await httpContext.Response.BodyWriter.FlushAsync();
        }
    }
}
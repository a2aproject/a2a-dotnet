using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace A2A.AspNetCore;

/// <summary>
/// Provides HTTP processing functionality for A2A (Agent-to-Agent) communication endpoints.
/// </summary>
public static class A2AHttpProcessor
{
    /// <summary>
    /// Gets the OpenTelemetry ActivitySource for HTTP processing operations.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("A2A.HttpProcessor", "1.0.0");

    /// <summary>
    /// Handles requests to retrieve agent card information.
    /// </summary>
    /// <param name="taskManager">The task manager instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="agentUrl">The URL of the agent.</param>
    /// <returns>A task representing the HTTP result containing the agent card information.</returns>
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
    /// Handles requests to retrieve a specific task by its identifier.
    /// </summary>
    /// <param name="taskManager">The task manager instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="id">The unique identifier of the task.</param>
    /// <param name="historyLength">The optional maximum number of history entries to retrieve.</param>
    /// <param name="metadata">Optional metadata as a JSON string.</param>
    /// <returns>A task representing the HTTP result containing the task information.</returns>
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
    /// Handles requests to cancel a specific task.
    /// </summary>
    /// <param name="taskManager">The task manager instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="id">The unique identifier of the task to cancel.</param>
    /// <returns>A task representing the HTTP result containing the cancelled task information.</returns>
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
    /// Handles requests to send a message to a task.
    /// </summary>
    /// <param name="taskManager">The task manager instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="taskId">The optional task identifier to send the message to.</param>
    /// <param name="sendParams">The message send parameters.</param>
    /// <param name="historyLength">The optional maximum number of history entries to retrieve.</param>
    /// <param name="metadata">Optional metadata as a JSON string.</param>
    /// <returns>A task representing the HTTP result containing the response.</returns>
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
    /// Handles requests to send a message to a task and receive real-time updates as Server-Sent Events.
    /// </summary>
    /// <param name="taskManager">The task manager instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="id">The unique identifier of the task to send the message to.</param>
    /// <param name="sendParams">The message send parameters.</param>
    /// <param name="historyLength">The optional maximum number of history entries to retrieve.</param>
    /// <param name="metadata">Optional metadata as a JSON string.</param>
    /// <returns>A task representing the HTTP result containing a stream of A2A events.</returns>
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
    /// Handles requests to resubscribe to an existing task to receive real-time updates.
    /// </summary>
    /// <param name="taskManager">The task manager instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="id">The unique identifier of the task to resubscribe to.</param>
    /// <returns>An HTTP result containing a stream of A2A events for the specified task.</returns>
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
    /// Handles requests to set push notification configuration for a task.
    /// </summary>
    /// <param name="taskManager">The task manager instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="id">The unique identifier of the task to configure push notifications for.</param>
    /// <param name="pushNotificationConfig">The push notification configuration to set.</param>
    /// <returns>A task representing the HTTP result containing the configured push notification settings.</returns>
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
    /// Handles requests to retrieve push notification configuration for a task.
    /// </summary>
    /// <param name="taskManager">The task manager instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="id">The unique identifier of the task to retrieve push notification configuration for.</param>
    /// <returns>A task representing the HTTP result containing the push notification configuration.</returns>
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
/// Represents an HTTP result that returns an A2A response as JSON.
/// </summary>
public class A2AResponseResult : IResult
{
    private readonly A2AResponse a2aResponse;

    /// <summary>
    /// Initializes a new instance of the <see cref="A2AResponseResult"/> class.
    /// </summary>
    /// <param name="a2aResponse">The A2A response to return.</param>
    public A2AResponseResult(A2AResponse a2aResponse)
    {
        this.a2aResponse = a2aResponse;
    }

    /// <summary>
    /// Executes the result by writing the A2A response as JSON to the HTTP response.
    /// </summary>
    /// <param name="httpContext">The HTTP context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.ContentType = "application/json";

        await JsonSerializer.SerializeAsync(httpContext.Response.Body, a2aResponse, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(A2AResponse)));
    }
}

/// <summary>
/// Represents an HTTP result that streams A2A events as Server-Sent Events.
/// </summary>
public class A2AEventStreamResult : IResult
{
    private readonly IAsyncEnumerable<A2AEvent> taskEvents;

    /// <summary>
    /// Initializes a new instance of the <see cref="A2AEventStreamResult"/> class.
    /// </summary>
    /// <param name="taskEvents">The asynchronous enumerable of A2A events to stream.</param>
    public A2AEventStreamResult(IAsyncEnumerable<A2AEvent> taskEvents)
    {
        this.taskEvents = taskEvents;
    }

    /// <summary>
    /// Executes the result by streaming A2A events as Server-Sent Events to the HTTP response.
    /// </summary>
    /// <param name="httpContext">The HTTP context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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
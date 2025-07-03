using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace A2A.AspNetCore;

public static class A2AHttpProcessor
{
    public static readonly ActivitySource ActivitySource = new("A2A.HttpProcessor", "1.0.0");

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

public class A2AResponseResult : IResult
{
    private readonly A2AResponse a2aResponse;

    public A2AResponseResult(A2AResponse a2aResponse)
    {
        this.a2aResponse = a2aResponse;
    }
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.ContentType = "application/json";

        await JsonSerializer.SerializeAsync(httpContext.Response.Body, a2aResponse, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(A2AResponse)));
    }
}

public class A2AEventStreamResult : IResult
{
    private readonly IAsyncEnumerable<A2AEvent> taskEvents;

    public A2AEventStreamResult(IAsyncEnumerable<A2AEvent> taskEvents)
    {
        this.taskEvents = taskEvents;
    }

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
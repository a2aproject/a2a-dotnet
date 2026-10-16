using A2A;
using System.Net;

namespace AgentServer;

internal sealed class ProactiveAgent() : IAgentHandler
{
    public Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) =>
        eventQueue.EnqueueTaskAsync(new AgentTask
        {
            Id = context.TaskId,
            ContextId = context.ContextId,
            History = [.. (context.Task?.History ?? []), context.Message],
            Artifacts = context.Task?.Artifacts,
            Metadata = context.Task?.Metadata,
            Status = context.Task?.Status ?? new A2A.TaskStatus
            {
                State = TaskState.Working,
                Timestamp = DateTimeOffset.UtcNow,
                Message = new Message
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    Role = Role.Agent,
                    TaskId = context.TaskId,
                    ContextId = context.ContextId,
                    Parts = [Part.FromText($"Waiting for external updates. POST a message to /proactive/tasks/{context.TaskId}/continue.")],
                },
            },
        }, cancellationToken).AsTask();

    public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) =>
        new TaskUpdater(eventQueue, context.TaskId, context.ContextId).CancelAsync(cancellationToken: cancellationToken).AsTask();

    public static AgentCard GetAgentCard(string agentUrl) => new()
    {
        Name = "Proactive Task Agent",
        Description = "Keeps a task open for external HTTP triggers that send agent messages through push notifications.",
        Version = "1.0.0",
        SupportedInterfaces =
        [
            new AgentInterface { Url = agentUrl, ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" },
        ],
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
        Capabilities = new() { Streaming = true, PushNotifications = true },
        Skills =
        [
            new AgentSkill
            {
                Id = "proactive",
                Name = "External task updates",
                Description = "Continues an existing task without fabricating an inbound A2A message.",
                Tags = ["proactive", "push-notifications", "sample"],
            },
        ],
    };

    public static void MapContinuationEndpoint(WebApplication app)
    {
        if (app.Services.GetRequiredService<IA2ARequestHandler>() is not A2AServer server)
        {
            throw new InvalidOperationException("The proactive sample requires the stock A2AServer.");
        }

        app.MapPost("/proactive/tasks/{taskId}/continue",
            async (string taskId, ContinueTaskRequest request, HttpContext http, CancellationToken cancellationToken) =>
            {
                // This unauthenticated trigger is for local demonstrations, not production authorization.
                if (http.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address))
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                        title: "The demo trigger only accepts loopback callers.");
                }
                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                        title: "A non-empty message is required.");
                }

                try
                {
                    var result = await server.ContinueTaskAsync(taskId, async (context, token) =>
                    {
                        await context.EventQueue.EnqueueMessageAsync(new Message
                        {
                            MessageId = Guid.NewGuid().ToString("N"),
                            Role = Role.Agent,
                            TaskId = context.TaskId,
                            ContextId = context.ContextId,
                            Parts = [Part.FromText(request.Message)],
                        }, token);
                    }, cancellationToken);

                    return Results.Ok(new
                    {
                        TaskId = result.Task.Id,
                        result.Task.ContextId,
                        result.PersistedEventCount,
                        result.AcceptedPushNotificationCount,
                    });
                }
                catch (TaskContinuationException exception)
                {
                    app.Logger.LogWarning(exception, "External continuation failed for task {TaskId}", taskId);
                    int status = exception.FailureKind switch
                    {
                        TaskContinuationFailureKind.TaskNotFound => StatusCodes.Status404NotFound,
                        TaskContinuationFailureKind.MissingPushRegistration or
                            TaskContinuationFailureKind.NonContinuableTask => StatusCodes.Status409Conflict,
                        TaskContinuationFailureKind.TaskStore or TaskContinuationFailureKind.PushNotificationStore or
                            TaskContinuationFailureKind.PushAdmission or TaskContinuationFailureKind.Cancellation =>
                                StatusCodes.Status503ServiceUnavailable,
                        _ => StatusCodes.Status500InternalServerError,
                    };
                    return Results.Problem(statusCode: status, title: "Task continuation failed.",
                        extensions: new Dictionary<string, object?>
                        {
                            ["taskId"] = taskId,
                            ["failureKind"] = exception.FailureKind.ToString(),
                            ["persistedEventCount"] = exception.Progress?.PersistedEventCount ?? 0,
                            ["acceptedPushNotificationCount"] = exception.Progress?.AcceptedPushNotificationCount ?? 0,
                        });
                }
            });
    }

    private sealed record ContinueTaskRequest(string? Message);
}

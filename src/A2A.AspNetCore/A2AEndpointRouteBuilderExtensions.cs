using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace A2A.AspNetCore;

public static class A2ARouteBuilderExtensions
{
    public static readonly ActivitySource ActivitySource = new("A2A.Endpoint", "1.0.0");

    /// <summary>
    /// Enables JSONRPC A2A endpoints for the specified path.
    /// </summary>
    /// <param name="endpoints"></param>
    /// <param name="taskManager"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public static IEndpointConventionBuilder MapA2A(this IEndpointRouteBuilder endpoints, TaskManager taskManager, string path)
    {
        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<IEndpointRouteBuilder>();

        var routeGroup = endpoints.MapGroup("");

        routeGroup.MapGet($"{path}/.well-known/agent.json", (HttpRequest request) =>
        {
            var agentUrl = $"{request.Scheme}://{request.Host}{request.Path}";
            var agentCard = taskManager.OnAgentCardQuery(agentUrl);
            return Results.Ok(agentCard);
        });

        routeGroup.MapPost(path, ([FromBody] JsonRpcRequest rpcRequest) => A2AJsonRpcProcessor.ProcessRequest(taskManager, rpcRequest));

        return routeGroup;
    }

    /// <summary>
    /// Enables experimental HTTP A2A endpoints for the specified path.
    /// </summary>
    /// <param name="endpoints"></param>
    /// <param name="taskManager"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public static IEndpointConventionBuilder MapHttpA2A(this IEndpointRouteBuilder endpoints, TaskManager taskManager, string path)
    {
        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<IEndpointRouteBuilder>();

        var routeGroup = endpoints.MapGroup(path);

        // /v1/card endpoint - Agent discovery
        routeGroup.MapGet("v1/card", async context => await A2AHttpProcessor.GetAgentCard(taskManager, logger, $"{context.Request.Scheme}://{context.Request.Host}{path}"));

        // /v1/tasks/{id} endpoint
        routeGroup.MapGet("v1/tasks/{id}", (string id, [FromQuery] int? historyLength, [FromQuery] string? metadata) =>
            A2AHttpProcessor.GetTask(taskManager, logger, id, historyLength, metadata));

        // /v1/tasks/{id}:cancel endpoint
        routeGroup.MapPost("v1/tasks/{id}:cancel", (string id) => A2AHttpProcessor.CancelTask(taskManager, logger, id));

        // /v1/message:send endpoint
        routeGroup.MapPost("v1/message:send", ([FromBody] MessageSendParams sendParams, int? historyLength, string? metadata) =>
            A2AHttpProcessor.SendTaskMessage(taskManager, logger, null, sendParams, historyLength, metadata));

        // /tasks/{id}/send endpoint
        routeGroup.MapPost("/tasks/{id}/send", (string id, [FromBody] MessageSendParams sendParams, int? historyLength, string? metadata) =>
            A2AHttpProcessor.SendTaskMessage(taskManager, logger, id, sendParams, historyLength, metadata));

        // /tasks/{id}/sendSubscribe endpoint
        routeGroup.MapPost("/tasks/{id}/sendSubscribe", (string id, [FromBody] MessageSendParams sendParams, int? historyLength, string? metadata) =>
            A2AHttpProcessor.SendSubscribeTaskMessage(taskManager, logger, id, sendParams, historyLength, metadata));

        // /tasks/{id}/resubscribe endpoint
        routeGroup.MapPost("/tasks/{id}/resubscribe", (string id) => A2AHttpProcessor.ResubscribeTask(taskManager, logger, id));

        // /tasks/{id}/pushNotification endpoint - PUT
        routeGroup.MapPut("/tasks/{id}/pushNotification", (string id, [FromBody] PushNotificationConfig pushNotificationConfig) =>
            A2AHttpProcessor.SetPushNotification(taskManager, logger, id, pushNotificationConfig));

        // /v1/tasks/{id}/pushNotification endpoint - GET
        routeGroup.MapGet("v1/tasks/{id}/pushNotificationConfigs", (string id) => A2AHttpProcessor.GetPushNotification(taskManager, logger, id));

        return routeGroup;
    }
}

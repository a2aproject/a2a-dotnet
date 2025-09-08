using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace A2A.AspNetCore;

/// <summary>
/// Extension methods for configuring A2A endpoints in ASP.NET Core applications.
/// </summary>
public static class A2ARouteBuilderExtensions
{
    /// <summary>
    /// Activity source for tracing A2A endpoint operations.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("A2A.Endpoint", "1.0.0");

    /// <summary>
    /// Enables JSON-RPC A2A endpoints for the specified path.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to configure.</param>
    /// <param name="taskManager">The task manager for handling A2A operations.</param>
    /// <param name="path">The base path for the A2A endpoints.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapA2A(this IEndpointRouteBuilder endpoints, ITaskManager taskManager, [StringSyntax("Route")] string path)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(taskManager);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<IEndpointRouteBuilder>();

        var routeGroup = endpoints.MapGroup("");

        routeGroup.MapPost(path, (HttpRequest request, CancellationToken cancellationToken) => A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, request, cancellationToken));

        return routeGroup;
    }

    /// <summary>
    /// Enables the well-known agent card endpoint for agent discovery.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to configure.</param>
    /// <param name="taskManager">The task manager for handling A2A operations.</param>
    /// <param name="agentPath">The base path where the A2A agent is hosted.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapWellKnownAgentCard(this IEndpointRouteBuilder endpoints, ITaskManager taskManager, [StringSyntax("Route")] string agentPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(taskManager);
        ArgumentException.ThrowIfNullOrEmpty(agentPath);

        var routeGroup = endpoints.MapGroup("");

        routeGroup.MapGet(".well-known/agent-card.json", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            var agentUrl = $"{request.Scheme}://{request.Host}{agentPath}";
            var agentCard = await taskManager.OnAgentCardQuery(agentUrl, cancellationToken);
            return Results.Ok(agentCard);
        });

        return routeGroup;
    }

    /// <summary>
    /// Enables experimental HTTP A2A endpoints for the specified path.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to configure.</param>
    /// <param name="taskManager">The task manager for handling A2A operations.</param>
    /// <param name="path">The base path for the HTTP A2A endpoints.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapHttpA2A(this IEndpointRouteBuilder endpoints, ITaskManager taskManager, [StringSyntax("Route")] string path)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(taskManager);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<IEndpointRouteBuilder>();

        var routeGroup = endpoints.MapGroup(path);

        // /v1/card endpoint - Agent discovery
        routeGroup.MapGet("/v1/card", async (HttpRequest request, CancellationToken cancellationToken) =>
            await A2AHttpProcessor.GetAgentCardAsync(taskManager, logger, $"{request.Scheme}://{request.Host}{path}", cancellationToken).ConfigureAwait(false));

        // /v1/tasks/{id} endpoint
        routeGroup.MapGet("/v1/tasks/{id}", (string id, [FromQuery] int? historyLength, [FromQuery] string? metadata, CancellationToken cancellationToken) =>
            A2AHttpProcessor.GetTaskAsync(taskManager, logger, id, historyLength, metadata, cancellationToken));

        // /v1/tasks/{id}:cancel endpoint
        routeGroup.MapPost("/v1/tasks/{id}:cancel", (string id, CancellationToken cancellationToken) => A2AHttpProcessor.CancelTaskAsync(taskManager, logger, id, cancellationToken));

        // /v1/tasks/{id}:subscribe endpoint
        routeGroup.MapGet("/v1/tasks/{id}:subscribe", (string id, CancellationToken cancellationToken) => A2AHttpProcessor.SubscribeTask(taskManager, logger, id, cancellationToken));

        // /v1/tasks/{id}/pushNotificationConfigs endpoint - POST
        routeGroup.MapPost("/v1/tasks/{id}/pushNotificationConfigs", (string id, [FromBody] PushNotificationConfig pushNotificationConfig, CancellationToken cancellationToken) =>
            A2AHttpProcessor.SetPushNotificationAsync(taskManager, logger, id, pushNotificationConfig, cancellationToken));

        // /v1/tasks/{id}/pushNotificationConfigs endpoint - GET
        routeGroup.MapGet("/v1/tasks/{id}/pushNotificationConfigs/{notificationConfigId?}", (string id, string? notificationConfigId, CancellationToken cancellationToken) =>
            A2AHttpProcessor.GetPushNotificationAsync(taskManager, logger, id, notificationConfigId, cancellationToken));

        // /v1/message:send endpoint
        routeGroup.MapPost("/v1/message:send", ([FromBody] MessageSendParams sendParams, CancellationToken cancellationToken) =>
            A2AHttpProcessor.SendMessageAsync(taskManager, logger, sendParams, cancellationToken));

        // /v1/message:stream endpoint
        routeGroup.MapPost("/v1/message:stream", ([FromBody] MessageSendParams sendParams, CancellationToken cancellationToken) =>
            A2AHttpProcessor.SendMessageStream(taskManager, logger, sendParams, cancellationToken));

        return routeGroup;
    }
}

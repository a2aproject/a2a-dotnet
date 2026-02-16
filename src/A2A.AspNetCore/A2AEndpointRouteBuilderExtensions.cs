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
    /// <summary>Activity source for tracing A2A endpoint operations.</summary>
    public static readonly ActivitySource ActivitySource = new("A2A.Endpoint", "1.0.0");

    /// <summary>Enables JSON-RPC A2A endpoints for the specified path.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="taskManager">The task manager implementation.</param>
    /// <param name="path">The route path for the A2A endpoint.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapA2A(this IEndpointRouteBuilder endpoints, ITaskManager taskManager, [StringSyntax("Route")] string path)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(taskManager);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var routeGroup = endpoints.MapGroup("");

        routeGroup.MapPost(path, (HttpRequest request, CancellationToken cancellationToken) => A2AJsonRpcProcessor.ProcessRequestAsync(taskManager, request, cancellationToken));

        return routeGroup;
    }

    /// <summary>Enables the well-known agent card endpoint for agent discovery.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="agentCard">The agent card to serve.</param>
    /// <param name="path">An optional route prefix. When provided, the agent card is served at <c>{path}/.well-known/agent-card.json</c>.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapWellKnownAgentCard(this IEndpointRouteBuilder endpoints, AgentCard agentCard, [StringSyntax("Route")] string path = "")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(agentCard);

        var routeGroup = endpoints.MapGroup(path);

        routeGroup.MapGet(".well-known/agent-card.json", () => Results.Ok(agentCard));

        return routeGroup;
    }

    /// <summary>
    /// Maps HTTP+JSON REST API endpoints for A2A v1.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="taskManager">The task manager implementation.</param>
    /// <param name="agentCard">The agent card to serve at the /v1/card endpoint.</param>
    /// <param name="path">The route prefix for all REST endpoints.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapHttpA2A(
        this IEndpointRouteBuilder endpoints, ITaskManager taskManager, AgentCard agentCard, [StringSyntax("Route")] string path = "")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(taskManager);
        ArgumentNullException.ThrowIfNull(agentCard);

        var routeGroup = endpoints.MapGroup(path);

        // Agent card
        routeGroup.MapGet("/v1/card", (CancellationToken ct)
            => A2AHttpProcessor.GetAgentCardRestAsync(taskManager, agentCard, ct));

        // Task operations
        routeGroup.MapGet("/v1/tasks/{id}", (string id, [FromQuery] int? historyLength, CancellationToken ct)
            => A2AHttpProcessor.GetTaskRestAsync(taskManager, id, historyLength, ct));

        routeGroup.MapPost("/v1/tasks/{id}:cancel", (string id, CancellationToken ct)
            => A2AHttpProcessor.CancelTaskRestAsync(taskManager, id, ct));

        routeGroup.MapGet("/v1/tasks/{id}:subscribe", (string id, CancellationToken ct)
            => A2AHttpProcessor.SubscribeToTaskRest(taskManager, id, ct));

        routeGroup.MapGet("/v1/tasks", ([FromQuery] string? contextId, [FromQuery] string? status,
            [FromQuery] int? pageSize, [FromQuery] string? pageToken, [FromQuery] int? historyLength,
            CancellationToken ct)
            => A2AHttpProcessor.ListTasksRestAsync(taskManager, contextId, status, pageSize, pageToken,
                historyLength, ct));

        // Message operations
        routeGroup.MapPost("/v1/message:send", ([FromBody] SendMessageRequest request, CancellationToken ct)
            => A2AHttpProcessor.SendMessageRestAsync(taskManager, request, ct));

        routeGroup.MapPost("/v1/message:stream", ([FromBody] SendMessageRequest request, CancellationToken ct)
            => A2AHttpProcessor.SendMessageStreamRest(taskManager, request, ct));

        // Push notification config operations
        routeGroup.MapPost("/v1/tasks/{id}/pushNotificationConfigs",
            (string id, [FromBody] PushNotificationConfig config, CancellationToken ct)
            => A2AHttpProcessor.CreatePushNotificationConfigRestAsync(taskManager, id, config, ct));

        routeGroup.MapGet("/v1/tasks/{id}/pushNotificationConfigs",
            (string id, [FromQuery] int? pageSize, [FromQuery] string? pageToken, CancellationToken ct)
            => A2AHttpProcessor.ListPushNotificationConfigRestAsync(taskManager, id, pageSize, pageToken, ct));

        routeGroup.MapGet("/v1/tasks/{id}/pushNotificationConfigs/{configId}",
            (string id, string configId, CancellationToken ct)
            => A2AHttpProcessor.GetPushNotificationConfigRestAsync(taskManager, id, configId, ct));

        routeGroup.MapDelete("/v1/tasks/{id}/pushNotificationConfigs/{configId}",
            (string id, string configId, CancellationToken ct)
            => A2AHttpProcessor.DeletePushNotificationConfigRestAsync(taskManager, id, configId, ct));

        // Extended agent card
        routeGroup.MapGet("/v1/extendedAgentCard", (CancellationToken ct)
            => A2AHttpProcessor.GetExtendedAgentCardRestAsync(taskManager, ct));

        return routeGroup;
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace A2A.AspNetCore;

/// <summary>
/// Extension methods for configuring A2A endpoints in ASP.NET Core applications.
/// </summary>
public static class A2ARouteBuilderExtensions
{
    /// <summary>
    /// Maps A2A JSON-RPC endpoint and well-known agent card using DI-registered services.
    /// Requires prior call to <see cref="A2AServiceCollectionExtensions.AddA2AAgent{THandler}"/>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="path">The route path for the A2A endpoint.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapA2A(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string path)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var handler = endpoints.ServiceProvider.GetRequiredService<IA2ARequestHandler>();
        var agentCard = endpoints.ServiceProvider.GetRequiredService<AgentCard>();

        var routeGroup = endpoints.MapGroup("");
        routeGroup.MapPost(path, (HttpRequest request, CancellationToken cancellationToken)
            => A2AJsonRpcProcessor.ProcessRequestAsync(handler, request, cancellationToken));

        return routeGroup;
    }

    /// <summary>Enables JSON-RPC A2A endpoints for the specified path.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="requestHandler">The A2A request handler.</param>
    /// <param name="path">The route path for the A2A endpoint.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapA2A(this IEndpointRouteBuilder endpoints, IA2ARequestHandler requestHandler, [StringSyntax("Route")] string path)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(requestHandler);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var routeGroup = endpoints.MapGroup("");

        routeGroup.MapPost(path, (HttpRequest request, CancellationToken cancellationToken) => A2AJsonRpcProcessor.ProcessRequestAsync(requestHandler, request, cancellationToken));

        return routeGroup;
    }

    /// <summary>Enables JSON-RPC A2A endpoints with per-request handler resolution.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="handlerFactory">Factory that resolves an <see cref="IA2ARequestHandler"/> per request from the <see cref="HttpContext"/>.</param>
    /// <param name="path">The route path for the A2A endpoint.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapA2A(
        this IEndpointRouteBuilder endpoints,
        Func<HttpContext, IA2ARequestHandler> handlerFactory,
        [StringSyntax("Route")] string path)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handlerFactory);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var routeGroup = endpoints.MapGroup("");
        routeGroup.MapPost(path, async (HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            IA2ARequestHandler handler;
            try
            {
                handler = handlerFactory(httpContext);
            }
            catch (A2AException ex)
            {
                return new JsonRpcResponseResult(JsonRpcResponse.CreateJsonRpcErrorResponse(new JsonRpcId((string?)null), ex));
            }

            return await A2AJsonRpcProcessor.ProcessRequestAsync(handler, httpContext.Request, cancellationToken);
        });

        return routeGroup;
    }

    /// <summary>Enables JSON-RPC A2A endpoints and well-known agent card with per-request resolution.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="handlerFactory">Factory that resolves an <see cref="IA2ARequestHandler"/> per request from the <see cref="HttpContext"/>.</param>
    /// <param name="agentCardFactory">Factory that resolves an <see cref="AgentCard"/> per request from the <see cref="HttpContext"/>.</param>
    /// <param name="path">The route path for the A2A JSON-RPC endpoint.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapA2A(
        this IEndpointRouteBuilder endpoints,
        Func<HttpContext, IA2ARequestHandler> handlerFactory,
        Func<HttpContext, AgentCard> agentCardFactory,
        [StringSyntax("Route")] string path)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handlerFactory);
        ArgumentNullException.ThrowIfNull(agentCardFactory);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var routeGroup = endpoints.MapGroup("");

        routeGroup.MapPost(path, async (HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            IA2ARequestHandler handler;
            try
            {
                handler = handlerFactory(httpContext);
            }
            catch (A2AException ex)
            {
                return new JsonRpcResponseResult(JsonRpcResponse.CreateJsonRpcErrorResponse(new JsonRpcId((string?)null), ex));
            }

            return await A2AJsonRpcProcessor.ProcessRequestAsync(handler, httpContext.Request, cancellationToken);
        });

        routeGroup.MapGet(".well-known/agent-card.json", (HttpContext httpContext) => Results.Ok(agentCardFactory(httpContext)));

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

    /// <summary>Enables the well-known agent card endpoint with per-request card resolution.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="agentCardFactory">Factory that resolves an <see cref="AgentCard"/> per request from the <see cref="HttpContext"/>.</param>
    /// <param name="path">An optional route prefix.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapWellKnownAgentCard(
        this IEndpointRouteBuilder endpoints,
        Func<HttpContext, AgentCard> agentCardFactory,
        [StringSyntax("Route")] string path = "")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(agentCardFactory);

        var routeGroup = endpoints.MapGroup(path);
        routeGroup.MapGet(".well-known/agent-card.json", (HttpContext httpContext) => Results.Ok(agentCardFactory(httpContext)));

        return routeGroup;
    }

    /// <summary>
    /// Maps HTTP+JSON REST API endpoints for A2A.
    /// </summary>
    /// <remarks>
    /// <para>Routes follow the A2A specification (e.g., <c>/tasks/{id}</c>, <c>/message:send</c>).
    /// Use the <paramref name="path"/> parameter to add a base path prefix if needed.</para>
    /// <para><strong>Limitation:</strong> Multi-tenant route variants
    /// (<c>/{tenant}/tasks/{id}</c>) defined in the A2A specification are not currently
    /// supported. The <c>Tenant</c> field on request types will always be <c>null</c>
    /// for REST API calls. Use the JSON-RPC binding with explicit tenant parameters
    /// if multi-tenant routing is required.</para>
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="requestHandler">The A2A request handler.</param>
    /// <param name="agentCard">The agent card to serve at the /card endpoint.</param>
    /// <param name="path">The route prefix for all REST endpoints.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapHttpA2A(
        this IEndpointRouteBuilder endpoints, IA2ARequestHandler requestHandler, AgentCard agentCard, [StringSyntax("Route")] string path = "")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(requestHandler);
        ArgumentNullException.ThrowIfNull(agentCard);

        var routeGroup = endpoints.MapGroup(path);
        var logger = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("A2A.REST");

        // Agent card (SDK convenience endpoint, not part of the A2A spec Section 11.3)
        routeGroup.MapGet("/card", (CancellationToken ct)
            => A2AHttpProcessor.GetAgentCardRestAsync(requestHandler, logger, agentCard, ct));

        // Task operations
        routeGroup.MapGet("/tasks/{id}", (string id, [FromQuery] int? historyLength, CancellationToken ct)
            => A2AHttpProcessor.GetTaskRestAsync(requestHandler, logger, id, historyLength, ct));

        routeGroup.MapPost("/tasks/{id}:cancel", (string id, CancellationToken ct)
            => A2AHttpProcessor.CancelTaskRestAsync(requestHandler, logger, id, ct));

        routeGroup.MapPost("/tasks/{id}:subscribe", (string id, CancellationToken ct)
            => A2AHttpProcessor.SubscribeToTaskRest(requestHandler, logger, id, ct));

        routeGroup.MapGet("/tasks", ([FromQuery] string? contextId, [FromQuery] string? status,
            [FromQuery] int? pageSize, [FromQuery] string? pageToken, [FromQuery] int? historyLength,
            CancellationToken ct)
            => A2AHttpProcessor.ListTasksRestAsync(requestHandler, logger, contextId, status, pageSize, pageToken,
                historyLength, ct));

        // Message operations
        routeGroup.MapPost("/message:send", ([FromBody] SendMessageRequest request, CancellationToken ct)
            => A2AHttpProcessor.SendMessageRestAsync(requestHandler, logger, request, ct));

        routeGroup.MapPost("/message:stream", ([FromBody] SendMessageRequest request, CancellationToken ct)
            => A2AHttpProcessor.SendMessageStreamRest(requestHandler, logger, request, ct));

        // Push notification config operations
        routeGroup.MapPost("/tasks/{id}/pushNotificationConfigs",
            (string id, [FromBody] PushNotificationConfig config, CancellationToken ct)
            => A2AHttpProcessor.CreatePushNotificationConfigRestAsync(requestHandler, logger, id, config, ct));

        routeGroup.MapGet("/tasks/{id}/pushNotificationConfigs",
            (string id, [FromQuery] int? pageSize, [FromQuery] string? pageToken, CancellationToken ct)
            => A2AHttpProcessor.ListPushNotificationConfigRestAsync(requestHandler, logger, id, pageSize, pageToken, ct));

        routeGroup.MapGet("/tasks/{id}/pushNotificationConfigs/{configId}",
            (string id, string configId, CancellationToken ct)
            => A2AHttpProcessor.GetPushNotificationConfigRestAsync(requestHandler, logger, id, configId, ct));

        routeGroup.MapDelete("/tasks/{id}/pushNotificationConfigs/{configId}",
            (string id, string configId, CancellationToken ct)
            => A2AHttpProcessor.DeletePushNotificationConfigRestAsync(requestHandler, logger, id, configId, ct));

        // Extended agent card
        routeGroup.MapGet("/extendedAgentCard", (CancellationToken ct)
            => A2AHttpProcessor.GetExtendedAgentCardRestAsync(requestHandler, logger, ct));

        return routeGroup;
    }

    /// <summary>
    /// Maps HTTP+JSON REST API endpoints for A2A with per-request handler and agent card resolution.
    /// </summary>
    /// <remarks>
    /// <para>Use this overload for multi-agent hosting scenarios where the handler and agent card
    /// vary per request (e.g., based on subdomain, authentication, or tenant context).</para>
    /// <para><strong>Limitation:</strong> Multi-tenant route variants
    /// (<c>/{tenant}/tasks/{id}</c>) defined in the A2A specification are not currently
    /// supported. The <c>Tenant</c> field on request types will always be <c>null</c>
    /// for REST API calls.</para>
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="handlerFactory">Factory that resolves an <see cref="IA2ARequestHandler"/> per request.</param>
    /// <param name="agentCardFactory">Factory that resolves an <see cref="AgentCard"/> per request.</param>
    /// <param name="path">The route prefix for all REST endpoints.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapHttpA2A(
        this IEndpointRouteBuilder endpoints,
        Func<HttpContext, IA2ARequestHandler> handlerFactory,
        Func<HttpContext, AgentCard> agentCardFactory,
        [StringSyntax("Route")] string path = "")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handlerFactory);
        ArgumentNullException.ThrowIfNull(agentCardFactory);

        var routeGroup = endpoints.MapGroup(path);
        var logger = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("A2A.REST");

        // Agent card
        routeGroup.MapGet("/card", (HttpContext httpContext, CancellationToken ct)
            => A2AHttpProcessor.GetAgentCardRestAsync(handlerFactory(httpContext), logger, agentCardFactory(httpContext), ct));

        // Task operations
        routeGroup.MapGet("/tasks/{id}", (HttpContext httpContext, string id, [FromQuery] int? historyLength, CancellationToken ct)
            => A2AHttpProcessor.GetTaskRestAsync(handlerFactory(httpContext), logger, id, historyLength, ct));

        routeGroup.MapPost("/tasks/{id}:cancel", (HttpContext httpContext, string id, CancellationToken ct)
            => A2AHttpProcessor.CancelTaskRestAsync(handlerFactory(httpContext), logger, id, ct));

        routeGroup.MapPost("/tasks/{id}:subscribe", (HttpContext httpContext, string id, CancellationToken ct)
            => A2AHttpProcessor.SubscribeToTaskRest(handlerFactory(httpContext), logger, id, ct));

        routeGroup.MapGet("/tasks", (HttpContext httpContext, [FromQuery] string? contextId, [FromQuery] string? status,
            [FromQuery] int? pageSize, [FromQuery] string? pageToken, [FromQuery] int? historyLength,
            CancellationToken ct)
            => A2AHttpProcessor.ListTasksRestAsync(handlerFactory(httpContext), logger, contextId, status, pageSize, pageToken,
                historyLength, ct));

        // Message operations
        routeGroup.MapPost("/message:send", (HttpContext httpContext, [FromBody] SendMessageRequest request, CancellationToken ct)
            => A2AHttpProcessor.SendMessageRestAsync(handlerFactory(httpContext), logger, request, ct));

        routeGroup.MapPost("/message:stream", (HttpContext httpContext, [FromBody] SendMessageRequest request, CancellationToken ct)
            => A2AHttpProcessor.SendMessageStreamRest(handlerFactory(httpContext), logger, request, ct));

        // Push notification config operations
        routeGroup.MapPost("/tasks/{id}/pushNotificationConfigs",
            (HttpContext httpContext, string id, [FromBody] PushNotificationConfig config, CancellationToken ct)
            => A2AHttpProcessor.CreatePushNotificationConfigRestAsync(handlerFactory(httpContext), logger, id, config, ct));

        routeGroup.MapGet("/tasks/{id}/pushNotificationConfigs",
            (HttpContext httpContext, string id, [FromQuery] int? pageSize, [FromQuery] string? pageToken, CancellationToken ct)
            => A2AHttpProcessor.ListPushNotificationConfigRestAsync(handlerFactory(httpContext), logger, id, pageSize, pageToken, ct));

        routeGroup.MapGet("/tasks/{id}/pushNotificationConfigs/{configId}",
            (HttpContext httpContext, string id, string configId, CancellationToken ct)
            => A2AHttpProcessor.GetPushNotificationConfigRestAsync(handlerFactory(httpContext), logger, id, configId, ct));

        routeGroup.MapDelete("/tasks/{id}/pushNotificationConfigs/{configId}",
            (HttpContext httpContext, string id, string configId, CancellationToken ct)
            => A2AHttpProcessor.DeletePushNotificationConfigRestAsync(handlerFactory(httpContext), logger, id, configId, ct));

        // Extended agent card
        routeGroup.MapGet("/extendedAgentCard", (HttpContext httpContext, CancellationToken ct)
            => A2AHttpProcessor.GetExtendedAgentCardRestAsync(handlerFactory(httpContext), logger, ct));

        return routeGroup;
    }
}

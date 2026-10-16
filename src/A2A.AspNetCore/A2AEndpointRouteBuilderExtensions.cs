using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace A2A.AspNetCore;

/// <summary>
/// Extension methods for configuring A2A endpoints in ASP.NET Core applications.
/// </summary>
public static class A2ARouteBuilderExtensions
{
    /// <summary>
    /// Maps A2A JSON-RPC endpoint and well-known agent card using DI-registered services.
    /// Requires prior call to <see cref="A2AServiceCollectionExtensions.AddA2AAgent{THandler}(IServiceCollection, AgentCard, Action{A2AServerOptions})"/>.
    /// </summary>
    /// <remarks>
    /// This a2a-dotnet endpoint selects tenant-specific agents through the endpoint URL and
    /// authenticated application context. It clears standard A2A tenant fields before dispatch.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="path">The route path for the A2A endpoint.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapA2A(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string path)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var handler = endpoints.ServiceProvider.GetRequiredService<IA2ARequestHandler>();
        if (endpoints.ServiceProvider.GetService<A2AExtensionOptions>() is { } extensionOptions)
        {
            return MapA2A(endpoints, handler, path, extensionOptions);
        }

        var routeGroup = endpoints.MapGroup("");
        routeGroup.MapPost(path, (HttpRequest request, CancellationToken cancellationToken)
            => A2AJsonRpcProcessor.ProcessRequestAsync(handler, request, cancellationToken));

        return routeGroup;
    }

    /// <summary>Enables JSON-RPC A2A endpoints for the specified path.</summary>
    /// <remarks>
    /// This a2a-dotnet endpoint selects tenant-specific agents through the endpoint URL and
    /// authenticated application context. It clears standard A2A tenant fields before dispatch.
    /// </remarks>
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

    /// <summary>
    /// Enables the JSON-RPC A2A endpoint with additional custom operation mappings.
    /// </summary>
    /// <remarks>
    /// This a2a-dotnet endpoint selects tenant-specific agents through the endpoint URL and
    /// authenticated application context. It clears tenant fields on standard A2A requests
    /// before dispatch. Custom operation models are not modified.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="requestHandler">The standard A2A request handler.</param>
    /// <param name="path">The route path for the A2A endpoint.</param>
    /// <param name="customRegistry">The custom operation registry.</param>
    /// <param name="customBindings">The custom JSON-RPC method mappings.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapA2A(
        this IEndpointRouteBuilder endpoints,
        IA2ARequestHandler requestHandler,
        [StringSyntax("Route")] string path,
        A2ACustomOperationRegistry customRegistry,
        A2AJsonRpcCustomOperationBindings customBindings)
    {
        ArgumentNullException.ThrowIfNull(customRegistry);
        ArgumentNullException.ThrowIfNull(customBindings);
        return MapA2A(endpoints, requestHandler, path, new A2AExtensionOptions(customRegistry), customBindings);
    }

    /// <summary>Maps JSON-RPC operations with request-scoped extension negotiation.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="requestHandler">The standard A2A request handler.</param>
    /// <param name="path">The JSON-RPC endpoint path.</param>
    /// <param name="extensionOptions">The authoritative registry and optional availability policy.</param>
    /// <param name="customBindings">Optional custom JSON-RPC method mappings.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    /// <remarks>
    /// Negotiation results are available through <see cref="HttpContext.Features"/> as
    /// an <see cref="A2ARequestContext"/> and through custom operation context features.
    /// Handler-only overloads remain negotiation-free.
    /// </remarks>
    public static IEndpointConventionBuilder MapA2A(
        this IEndpointRouteBuilder endpoints,
        IA2ARequestHandler requestHandler,
        [StringSyntax("Route")] string path,
        A2AExtensionOptions extensionOptions,
        A2AJsonRpcCustomOperationBindings? customBindings = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(requestHandler);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(extensionOptions);
        customBindings?.EnsureRegistryOwnership(extensionOptions.Registry);

        var routeGroup = endpoints.MapGroup("");
        routeGroup.MapPost(path, (HttpRequest request, CancellationToken cancellationToken) =>
            A2AJsonRpcProcessor.ProcessRequestWithExtensionsAsync(
                requestHandler,
                request,
                extensionOptions,
                customBindings,
                cancellationToken));

        return routeGroup;
    }

    /// <summary>Enables the well-known agent card endpoint for agent discovery.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="agentCard">The agent card to serve.</param>
    /// <param name="path">An optional route prefix. When provided, the agent card is served at <c>{path}/.well-known/agent-card.json</c>.</param>
    /// <param name="cacheOptions">Optional Agent Card HTTP caching configuration.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapWellKnownAgentCard(
        this IEndpointRouteBuilder endpoints,
        AgentCard agentCard,
        [StringSyntax("Route")] string path = "",
        AgentCardCacheOptions? cacheOptions = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(agentCard);

        var routeGroup = endpoints.MapGroup(path);
        var lastModified = DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture);
        var cacheControl = GetAgentCardCacheControl(cacheOptions);

        routeGroup.MapGet(".well-known/agent-card.json", (HttpResponse response) =>
        {
            var json = JsonSerializer.Serialize(
                agentCard,
                A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(AgentCard)));
            var jsonBytes = Encoding.UTF8.GetBytes(json);

            response.Headers.CacheControl = cacheControl;
            response.Headers.ETag = $"\"{Convert.ToHexString(SHA256.HashData(jsonBytes))}\"";
            response.Headers.LastModified = lastModified;
            return Results.Bytes(jsonBytes, "application/json");
        });

        return routeGroup;
    }

    /// <summary>Maps a registry-consistent, detached well-known Agent Card.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="registry">The authoritative extension registry.</param>
    /// <param name="agentCard">The host-provided Agent Card.</param>
    /// <param name="path">An optional route prefix.</param>
    /// <param name="cacheOptions">Optional HTTP caching configuration.</param>
    /// <returns>An endpoint convention builder.</returns>
    /// <remarks>Signed cards are not rewritten; declaration mismatches require updating and re-signing the card.</remarks>
    public static IEndpointConventionBuilder MapWellKnownAgentCard(
        this IEndpointRouteBuilder endpoints, A2ACustomOperationRegistry registry, AgentCard agentCard,
        [StringSyntax("Route")] string path = "", AgentCardCacheOptions? cacheOptions = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(registry);
        return MapWellKnownAgentCard(endpoints, registry.PrepareAgentCard(agentCard), path, cacheOptions);
    }

    private static string GetAgentCardCacheControl(AgentCardCacheOptions? cacheOptions)
    {
        var maxAge = cacheOptions?.MaxAge ?? TimeSpan.FromHours(1);
        if (maxAge < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cacheOptions),
                maxAge,
                "Agent Card cache max-age cannot be negative.");
        }

        return $"public, max-age={(long)Math.Ceiling(maxAge.TotalSeconds)}";
    }

    /// <summary>
    /// Maps HTTP+JSON REST API endpoints for A2A.
    /// </summary>
    /// <remarks>
    /// <para>Routes follow the A2A specification (e.g., <c>/tasks/{id}</c>, <c>/message:send</c>).
    /// Use the <paramref name="path"/> parameter to add a base path prefix if needed.</para>
    /// <para>This a2a-dotnet endpoint selects a tenant-specific agent through its URL,
    /// with routing configured by the host application. It clears standard A2A tenant fields
    /// before dispatch.</para>
    /// <para><strong>Limitation:</strong> This method does not automatically register
    /// tenant-parameter route variants or implement tenant selection from request fields.
    /// The host application is responsible for mapping tenant-specific agent URLs.</para>
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="requestHandler">The A2A request handler.</param>
    /// <param name="path">The route prefix for all REST endpoints.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapHttpA2A(
        this IEndpointRouteBuilder endpoints, IA2ARequestHandler requestHandler, [StringSyntax("Route")] string path = "")
        => MapHttpA2ACore(endpoints, requestHandler, path, registry: null);

    private static RouteGroupBuilder MapHttpA2ACore(
        IEndpointRouteBuilder endpoints, IA2ARequestHandler requestHandler, string path,
        A2ACustomOperationRegistry? registry)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(requestHandler);
        ArgumentNullException.ThrowIfNull(path);

        var routeGroup = endpoints.MapGroup(path);
        var logger = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("A2A.REST");

        // Reject unsupported A2A-Version header values on the HTTP+JSON binding, matching the
        // JSON-RPC binding (A2AJsonRpcProcessor.CheckPreflight). See GitHub issue #512.
        AddA2AVersionFilter(routeGroup);

        // Task operations
        IA2ARequestHandler HandlerFor(HttpContext context) => registry is null
            ? requestHandler
            : requestHandler.WithRequestContext(context.Features.Get<A2ARequestContext>());

        routeGroup.MapGet("/tasks/{id}", (HttpContext context, string id, [FromQuery] int? historyLength, CancellationToken ct)
            => A2AHttpProcessor.GetTaskRestAsync(HandlerFor(context), logger, id, historyLength, ct));

        routeGroup.MapPost("/tasks/{id}:cancel", (HttpContext context, string id, CancellationToken ct)
            => A2AHttpProcessor.CancelTaskRestAsync(HandlerFor(context), logger, id, ct));

        routeGroup.MapPost("/tasks/{id}:subscribe", (HttpContext context, string id, CancellationToken ct)
            => A2AHttpProcessor.SubscribeToTaskRest(HandlerFor(context), logger, id, ct));

        routeGroup.MapGet("/tasks", (HttpContext context, [FromQuery] string? contextId, [FromQuery] string? status,
            [FromQuery] int? pageSize, [FromQuery] string? pageToken, [FromQuery] int? historyLength,
            [FromQuery] DateTimeOffset? statusTimestampAfter, [FromQuery] bool? includeArtifacts,
            CancellationToken ct)
            => A2AHttpProcessor.ListTasksRestAsync(HandlerFor(context), logger, contextId, status, pageSize, pageToken,
                historyLength, statusTimestampAfter, includeArtifacts, ct));

        // Message operations
        routeGroup.MapPost("/message:send", (HttpContext context, [FromBody] SendMessageRequest request, CancellationToken ct)
            => A2AHttpProcessor.SendMessageRestAsync(HandlerFor(context), logger, request, ct));

        routeGroup.MapPost("/message:stream", (HttpContext context, [FromBody] SendMessageRequest request, CancellationToken ct)
            => A2AHttpProcessor.SendMessageStreamRest(HandlerFor(context), logger, request, ct));

        // Push notification config operations
        routeGroup.MapPost("/tasks/{id}/pushNotificationConfigs",
            (HttpContext context, string id, [FromBody] TaskPushNotificationConfig config, CancellationToken ct)
            => A2AHttpProcessor.CreatePushNotificationConfigRestAsync(HandlerFor(context), logger, id, config, ct));

        routeGroup.MapGet("/tasks/{id}/pushNotificationConfigs",
            (HttpContext context, string id, [FromQuery] int? pageSize, [FromQuery] string? pageToken, CancellationToken ct)
            => A2AHttpProcessor.ListPushNotificationConfigRestAsync(HandlerFor(context), logger, id, pageSize, pageToken, ct));

        routeGroup.MapGet("/tasks/{id}/pushNotificationConfigs/{configId}",
            (HttpContext context, string id, string configId, CancellationToken ct)
            => A2AHttpProcessor.GetPushNotificationConfigRestAsync(HandlerFor(context), logger, id, configId, ct));

        routeGroup.MapDelete("/tasks/{id}/pushNotificationConfigs/{configId}",
            (HttpContext context, string id, string configId, CancellationToken ct)
            => A2AHttpProcessor.DeletePushNotificationConfigRestAsync(HandlerFor(context), logger, id, configId, ct));

        // Extended agent card
        routeGroup.MapGet("/extendedAgentCard", (HttpContext context, CancellationToken ct)
            => A2AHttpProcessor.GetExtendedAgentCardRestAsync(HandlerFor(context), logger, ct, registry));

        return routeGroup;
    }

    /// <summary>
    /// Maps the standard HTTP+JSON A2A endpoints and additional custom operation routes.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="requestHandler">The standard A2A request handler.</param>
    /// <param name="path">The route prefix for all standard and custom HTTP endpoints.</param>
    /// <param name="customRegistry">The custom operation registry.</param>
    /// <param name="customBindings">The custom HTTP route mappings.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapHttpA2A(
        this IEndpointRouteBuilder endpoints,
        IA2ARequestHandler requestHandler,
        [StringSyntax("Route")] string path,
        A2ACustomOperationRegistry customRegistry,
        A2AHttpCustomOperationBindings customBindings)
    {
        ArgumentNullException.ThrowIfNull(customRegistry);
        ArgumentNullException.ThrowIfNull(customBindings);
        return MapHttpA2A(endpoints, requestHandler, path, new A2AExtensionOptions(customRegistry), customBindings);
    }

    /// <summary>Maps HTTP+JSON operations with request-scoped extension negotiation.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="requestHandler">The standard A2A request handler.</param>
    /// <param name="path">The route prefix for standard and custom HTTP endpoints.</param>
    /// <param name="extensionOptions">The authoritative registry and optional availability policy.</param>
    /// <param name="customBindings">Optional custom HTTP route mappings.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    /// <remarks>
    /// Negotiation results are available through <see cref="HttpContext.Features"/> as
    /// an <see cref="A2ARequestContext"/> and through custom operation context features.
    /// Handler-only overloads remain negotiation-free.
    /// </remarks>
    public static IEndpointConventionBuilder MapHttpA2A(
        this IEndpointRouteBuilder endpoints,
        IA2ARequestHandler requestHandler,
        [StringSyntax("Route")] string path,
        A2AExtensionOptions extensionOptions,
        A2AHttpCustomOperationBindings? customBindings = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(requestHandler);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(extensionOptions);
        customBindings?.EnsureRegistryOwnership(extensionOptions.Registry);

        var routeGroup = endpoints.MapGroup(path);
        var logger = endpoints.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("A2A.REST.Custom");
        var customRegistry = extensionOptions.Registry;
        var standardRouteGroup = MapHttpA2ACore(routeGroup, requestHandler, "", customRegistry);
        AddExtensionNegotiationFilter(standardRouteGroup, extensionOptions, logger);

        if (customBindings is null) return routeGroup;

        var customRouteGroup = routeGroup.MapGroup("");
        AddA2AVersionFilter(customRouteGroup);

        foreach (var binding in customBindings.Items)
        {
            customRouteGroup.MapMethods(
                binding.Route,
                [binding.HttpMethod],
                async context =>
                {
                    try
                    {
                        await A2AExtensionNegotiation.NegotiateAsync(
                            context, extensionOptions, context.RequestAborted).ConfigureAwait(false);
                        var operationContext = A2AExtensionNegotiation.CreateOperationContext(context);
                        object? request;
                        try
                        {
                            request = await binding.BindAsync(
                                context,
                                context.RequestAborted).ConfigureAwait(false);
                        }
                        catch (JsonException exception)
                        {
                            throw new A2AException(
                                "The custom operation request body is invalid.",
                                exception,
                                A2AErrorCode.InvalidParams);
                        }

                        IResult result;
                        if (binding.Registration.Kind == A2ACustomOperationKind.Streaming)
                        {
                            result = new CustomHttpStreamedResult(
                                customRegistry.InvokeStreamingAsync(
                                    binding.Registration,
                                    operationContext,
                                    request!,
                                    context.RequestAborted),
                                binding.Registration.OutputTypeInfo,
                                logger);
                        }
                        else
                        {
                            var response = await customRegistry.InvokeAsync(
                                binding.Registration,
                                operationContext,
                                request!,
                                context.RequestAborted).ConfigureAwait(false);
                            result = new CustomHttpJsonResult(
                                response,
                                binding.Registration.OutputTypeInfo);
                        }

                        await result.ExecuteAsync(context).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                    {
                        // Client disconnected.
                    }
                    catch (A2AException exception)
                    {
                        await new A2AErrorResult(exception)
                            .ExecuteAsync(context)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        logger.UnexpectedErrorInActivityName(exception, "custom HTTP operation");
                        await new A2AErrorResult(
                            new A2AException(
                                "An internal error occurred.",
                                A2AErrorCode.InternalError))
                            .ExecuteAsync(context)
                            .ConfigureAwait(false);
                    }
                });
        }

        return routeGroup;
    }

    private static void AddExtensionNegotiationFilter(
        RouteGroupBuilder routeGroup,
        A2AExtensionOptions extensionOptions,
        ILogger logger)
    {
        routeGroup.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            try
            {
                await A2AExtensionNegotiation.NegotiateAsync(
                    httpContext, extensionOptions, httpContext.RequestAborted).ConfigureAwait(false);
                return await next(context).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (A2AException exception)
            {
                return new A2AErrorResult(exception);
            }
            catch (Exception exception)
            {
                logger.UnexpectedErrorInActivityName(exception, "extension negotiation");
                return new A2AErrorResult(new A2AException("An internal error occurred.", A2AErrorCode.InternalError));
            }
        });
    }

    private static void AddA2AVersionFilter(RouteGroupBuilder routeGroup)
    {
        routeGroup.AddEndpointFilter(static async (context, next) =>
        {
            var error = A2AVersionHeader.Validate(
                context.HttpContext.Request.Headers[A2AVersionHeader.HeaderName]);
            return error is not null ? new A2AErrorResult(error) : await next(context);
        });
    }
}

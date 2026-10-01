using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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
        return endpoints.MapA2A(handler, path);
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

        var dispatch = A2AJsonRpcProcessor.CreateStandardDispatch();
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(requestHandler)));
        return endpoints.MapA2A(
            scopeFactory,
            dispatch.Handlers,
            dispatch.Bindings,
            path);
    }

    /// <summary>Enables request-scoped JSON-RPC A2A endpoints with typed operations.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="scopeFactory">The request-scope factory.</param>
    /// <param name="handlers">The operation handlers.</param>
    /// <param name="bindings">The JSON-RPC operation bindings.</param>
    /// <param name="path">The route path for the A2A endpoint.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapA2A(
        this IEndpointRouteBuilder endpoints,
        A2ARequestScopeFactory scopeFactory,
        A2AOperationHandlerCatalog handlers,
        A2AJsonRpcOperationBindings bindings,
        [StringSyntax("Route")] string path)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentException.ThrowIfNullOrEmpty(path);

        bindings.Validate(handlers.OperationCatalog);

        var routeGroup = endpoints.MapGroup("");
        routeGroup.MapPost(
            path,
            (HttpRequest request, CancellationToken cancellationToken) =>
                A2AJsonRpcProcessor.ProcessRequestAsync(
                    scopeFactory,
                    handlers,
                    bindings,
                    request,
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
    /// <para>For JSON-RPC and HTTP+JSON, select a tenant-specific agent through its URL,
    /// with routing configured by the host application. Explicit tenant parameters are
    /// intended for the gRPC binding, not tenant selection in the HTTP bindings.</para>
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
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(requestHandler);
        ArgumentNullException.ThrowIfNull(path);

        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .AddStandardA2AHandlers(standard)
            .Build(operationCatalog);
        var bindings = new A2AHttpOperationBindingBuilder()
            .AddStandardA2AHttpBindings(standard)
            .Build();
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(requestHandler)));
        return endpoints.MapHttpA2A(
            scopeFactory,
            handlers,
            bindings,
            path);
    }

    /// <summary>Maps standard and custom HTTP+JSON operations using request-specific state.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="scopeFactory">The request-scope factory.</param>
    /// <param name="handlers">The operation handlers.</param>
    /// <param name="bindings">The HTTP operation bindings.</param>
    /// <param name="path">The route prefix for all operations.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapHttpA2A(
        this IEndpointRouteBuilder endpoints,
        A2ARequestScopeFactory scopeFactory,
        A2AOperationHandlerCatalog handlers,
        A2AHttpOperationBindings bindings,
        [StringSyntax("Route")] string path = "")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(path);

        var routeGroup = endpoints.MapGroup(path);
        bindings.MapEndpoints(routeGroup, scopeFactory, handlers);
        return routeGroup;
    }
}

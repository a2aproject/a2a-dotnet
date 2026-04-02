namespace A2A.V0_3Compat;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Extension methods for registering A2A endpoints with v0.3 compatibility support.
/// </summary>
public static class V03ServerCompatEndpointExtensions
{
    /// <summary>
    /// Maps an A2A JSON-RPC endpoint that accepts both v0.3 and v1.0 client requests.
    /// v0.3 requests are automatically translated to v1.0 before being handled, and responses
    /// are translated back to v0.3 wire format.
    /// </summary>
    /// <remarks>
    /// Use this instead of <c>MapA2A</c> when you need to support v0.3 clients during a migration
    /// period. Once all clients have been upgraded to v1.0, switch back to <c>MapA2A</c>.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="requestHandler">The v1.0 A2A request handler.</param>
    /// <param name="path">The route path for the endpoint.</param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    [RequiresDynamicCode("MapA2AWithV03Compat uses runtime reflection for route binding. For AOT-compatible usage, use a source-generated host.")]
    [RequiresUnreferencedCode("MapA2AWithV03Compat may perform reflection on types that are not preserved by trimming.")]
    public static IEndpointConventionBuilder MapA2AWithV03Compat(
        this IEndpointRouteBuilder endpoints,
        IA2ARequestHandler requestHandler,
        [StringSyntax("Route")] string path)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(requestHandler);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var routeGroup = endpoints.MapGroup("");
        routeGroup.MapPost(path, (HttpRequest request, CancellationToken cancellationToken)
            => V03ServerProcessor.ProcessRequestAsync(requestHandler, request, cancellationToken));

        return routeGroup;
    }

    /// <summary>
    /// Maps agent card discovery endpoints that serve the agent card in v0.3 or v1.0 format
    /// based on the <c>A2A-Version</c> request header, supporting both client versions simultaneously.
    /// Registers both <c>GET {path}/</c> and <c>GET {path}/.well-known/agent-card.json</c>.
    /// </summary>
    /// <remarks>
    /// <para>Format negotiation is based on the <c>A2A-Version</c> request header:</para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>GET {path}/</c>: returns v0.3 by default (absent header indicates a v0.3 client per spec);
    /// returns v1.0 when <c>A2A-Version: 1.0</c> is present.
    /// </description></item>
    /// <item><description>
    /// <c>GET {path}/.well-known/agent-card.json</c>: returns v0.3 by default (backward compatibility for
    /// v0.3 clients that do not send the header); returns v1.0 when <c>A2A-Version: 1.0</c> is present
    /// (as sent by <see cref="A2AClientFactory.CreateAsync"/>).
    /// </description></item>
    /// </list>
    /// <para>Use this instead of the host's v1.0 agent card method during a v0.3-to-v1.0 migration
    /// period. Once all clients have upgraded to v1.0, replace this call with the host's v1.0
    /// equivalent to remove the header-based negotiation.</para>
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="getAgentCardAsync">A factory that returns the v1.0 agent card to serve or convert.</param>
    /// <param name="path">The route prefix for the agent card endpoints.</param>
    /// <param name="blendedCard">
    /// When <c>true</c> (default), the v0.3 response includes both v0.3 fields and the v1.0
    /// <c>supportedInterfaces</c> property side-by-side. This allows v1.0 clients to read the card
    /// even when no <c>A2A-Version</c> header is sent. Set to <c>false</c> to return a strict v0.3
    /// card with no v1.0 properties, for clients whose deserializers reject unknown fields.
    /// </param>
    /// <returns>An endpoint convention builder for further configuration.</returns>
    [RequiresDynamicCode("MapAgentCardGetWithV03Compat uses runtime reflection for route binding. For AOT-compatible usage, use a source-generated host.")]
    [RequiresUnreferencedCode("MapAgentCardGetWithV03Compat may perform reflection on types that are not preserved by trimming.")]
    public static IEndpointConventionBuilder MapAgentCardGetWithV03Compat(
        this IEndpointRouteBuilder endpoints,
        Func<Task<AgentCard>> getAgentCardAsync,
        [StringSyntax("Route")] string path = "",
        bool blendedCard = true)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(getAgentCardAsync);

        var routeGroup = endpoints.MapGroup(path);

        // Negotiate format via A2A-Version header.
        // Per spec, v1.0 clients MUST send A2A-Version; absent header indicates a v0.3 client.
        // Explicit A2A-Version: 0.3 always returns strict v0.3; blendedCard only applies when
        // no header is present (client version unknown).
        routeGroup.MapGet(string.Empty, async (HttpRequest request) =>
        {
            var v1Card = await getAgentCardAsync();
            var version = request.Headers["A2A-Version"].FirstOrDefault();
            if (version == "1.0")
                return Results.Ok(v1Card);
            if (version == "0.3")
                return Results.Ok(V03TypeConverter.ToV03AgentCard(v1Card));
            return blendedCard
                ? Results.Json(V03TypeConverter.ToBlendedAgentCard(v1Card))
                : Results.Ok(V03TypeConverter.ToV03AgentCard(v1Card));
        });

        // Both v0.3 and v1.0 clients use GET .well-known/agent-card.json.
        // v1.0 clients (A2AClientFactory.CreateAsync) send A2A-Version: 1.0; return v1.0 format.
        // Explicit A2A-Version: 0.3 returns strict v0.3; absent header defaults to blended or strict
        // depending on blendedCard.
        routeGroup.MapGet(".well-known/agent-card.json", async (HttpRequest request, CancellationToken ct) =>
        {
            var v1Card = await getAgentCardAsync();
            var version = request.Headers["A2A-Version"].FirstOrDefault();
            if (version == "1.0")
                return Results.Ok(v1Card);
            if (version == "0.3")
                return Results.Ok(V03TypeConverter.ToV03AgentCard(v1Card));
            return blendedCard
                ? Results.Json(V03TypeConverter.ToBlendedAgentCard(v1Card))
                : Results.Ok(V03TypeConverter.ToV03AgentCard(v1Card));
        });

        return routeGroup;
    }
}

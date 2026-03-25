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
}

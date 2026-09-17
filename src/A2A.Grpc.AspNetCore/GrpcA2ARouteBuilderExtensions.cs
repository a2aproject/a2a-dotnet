namespace Microsoft.AspNetCore.Builder;

using A2A.Grpc.AspNetCore;
using global::Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for wiring the A2A gRPC binding into an ASP.NET Core application, mirroring the
/// <c>MapA2A</c> (JSON-RPC) and <c>MapHttpA2A</c> (HTTP+JSON) extensions.
/// </summary>
public static class GrpcA2ARouteBuilderExtensions
{
    /// <summary>
    /// Adds ASP.NET Core gRPC services required to host the A2A gRPC binding. Call this alongside the
    /// core A2A agent registration (e.g. <c>AddA2AAgent</c>) so an <see cref="A2A.IA2ARequestHandler"/> is available.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>An <see cref="IGrpcServerBuilder"/> for further gRPC configuration.</returns>
    public static IGrpcServerBuilder AddA2AGrpc(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddGrpc();
    }

    /// <summary>
    /// Maps the A2A gRPC service onto the endpoint pipeline. Requires <see cref="AddA2AGrpc"/> (or
    /// <c>AddGrpc</c>) and a registered <see cref="A2A.IA2ARequestHandler"/> in the service provider.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>A builder for configuring the gRPC service endpoints.</returns>
    public static GrpcServiceEndpointConventionBuilder MapGrpcA2A(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapGrpcService<A2AGrpcService>();
    }
}

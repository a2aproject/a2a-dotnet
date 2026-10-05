namespace Microsoft.AspNetCore.Builder;

using A2A.Grpc.AspNetCore;
using global::Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for wiring the A2A gRPC binding into an ASP.NET Core application, mirroring the
/// <c>MapA2A</c> (JSON-RPC) and <c>MapHttpA2A</c> (HTTP+JSON) extensions.
/// </summary>
/// <remarks>
/// gRPC requires HTTP/2, whereas the JSON-RPC and HTTP+JSON bindings in <c>A2A.AspNetCore</c> work over
/// plain HTTP/1.1. All three bindings can be mapped on the same <see cref="WebApplication"/> and share the
/// same registered <see cref="A2A.IA2ARequestHandler"/>, but Kestrel must be configured to accept both
/// protocols on whatever endpoint(s) you expose — for example by enabling
/// <c>HttpProtocols.Http1AndHttp2</c> on a TLS endpoint (protocol selection via ALPN), or by mapping the
/// gRPC and JSON-RPC/HTTP+JSON bindings to separate Kestrel endpoints/ports with their own
/// <c>HttpProtocols</c> settings.
/// </remarks>
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
    /// <remarks>
    /// This endpoint requires HTTP/2. If the same <see cref="WebApplication"/> also maps the JSON-RPC
    /// (<c>MapA2A</c>) or HTTP+JSON (<c>MapHttpA2A</c>) bindings — which work over HTTP/1.1 — configure
    /// Kestrel so the relevant endpoint(s) accept both protocols (e.g. <c>HttpProtocols.Http1AndHttp2</c>
    /// with TLS/ALPN), or host gRPC on a separate Kestrel endpoint/port configured for HTTP/2.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>A builder for configuring the gRPC service endpoints.</returns>
    public static GrpcServiceEndpointConventionBuilder MapGrpcA2A(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapGrpcService<A2AGrpcService>();
    }

    /// <summary>
    /// Registers the handler catalog and binding table needed to host custom/extension A2A operations over
    /// the gRPC envelope service. Call this alongside <see cref="AddA2AGrpc"/> when the application defines
    /// operations beyond the standard A2A set (see <see cref="A2A.A2AOperationCatalogBuilder.DefineUnary{TRequest,TResult}"/>/<c>DefineStreaming</c>)
    /// that should also be reachable over gRPC.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="AddA2AGrpc"/>/<see cref="MapGrpcA2A"/>, which dispatch standard operations through
    /// an internally-built catalog, the extension envelope dispatches application-defined operations, so the
    /// application's own <see cref="A2A.A2AOperationHandlerCatalog"/> and
    /// <see cref="A2AGrpcExtensionOperationBindings"/> (built with <see cref="A2AGrpcExtensionOperationBindingBuilder"/>)
    /// must be supplied. Because <see cref="IEndpointRouteBuilder.MapGrpcService{TService}"/> constructs the
    /// gRPC service purely through dependency injection, both are registered as singletons here rather than
    /// passed as explicit parameters the way <c>MapA2A</c>/<c>MapHttpA2A</c> do.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="handlers">The handler catalog backing the application's custom/extension operations.</param>
    /// <param name="bindings">The gRPC envelope binding table for those operations.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddA2AGrpcExtensions(
        this IServiceCollection services,
        A2A.A2AOperationHandlerCatalog handlers,
        A2AGrpcExtensionOperationBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(bindings);

        services.AddSingleton(handlers);
        services.AddSingleton(bindings);
        return services;
    }

    /// <summary>
    /// Maps the gRPC envelope service for custom/extension A2A operations onto the endpoint pipeline.
    /// Requires <see cref="AddA2AGrpc"/> (or <c>AddGrpc</c>), <see cref="AddA2AGrpcExtensions"/>, and a
    /// registered <see cref="A2A.IA2ARequestHandler"/> in the service provider.
    /// </summary>
    /// <remarks>
    /// Subject to the same HTTP/2 requirements and Kestrel configuration notes as <see cref="MapGrpcA2A"/>.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>A builder for configuring the gRPC service endpoints.</returns>
    public static GrpcServiceEndpointConventionBuilder MapGrpcA2AExtensions(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapGrpcService<A2AGrpcExtensionService>();
    }
}

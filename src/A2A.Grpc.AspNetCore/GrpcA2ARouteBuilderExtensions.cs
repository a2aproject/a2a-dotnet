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

    /// <summary>Registers A2A gRPC services with request-scoped extension negotiation.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="extensionOptions">The authoritative registry and optional availability policy.</param>
    /// <returns>A builder for further gRPC configuration.</returns>
    /// <remarks>
    /// Applies negotiation to the standard A2A service and generic custom operation service,
    /// not to other gRPC services. Register an IA2ARequestHandler separately for standard calls.
    /// The parameterless overload remains negotiation-free.
    /// </remarks>
    public static IGrpcServerBuilder AddA2AGrpc(
        this IServiceCollection services,
        A2AGrpcExtensionOptions extensionOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(extensionOptions);
        services.AddSingleton(extensionOptions.Registry);
        return services.AddGrpc()
            .WithA2AExtensions<A2AGrpcService>(extensionOptions)
            .WithA2AExtensions<A2AGrpcCustomOperationService>(extensionOptions);
    }

    /// <summary>Adds extension negotiation to a dedicated unary/server-streaming gRPC service.</summary>
    /// <typeparam name="TService">The dedicated gRPC service implementation.</typeparam>
    /// <param name="builder">The gRPC server builder.</param>
    /// <param name="extensionOptions">The authoritative registry and optional availability policy.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <remarks>
    /// The service should use CreateA2AOperationContext on its ServerCallContext when invoking
    /// registry operations. This does not register the service's own dependencies or map its endpoint.
    /// Repeated equivalent configuration is idempotent; conflicting registries or policies are rejected.
    /// </remarks>
    public static IGrpcServerBuilder WithA2AExtensions<TService>(
        this IGrpcServerBuilder builder,
        A2AGrpcExtensionOptions extensionOptions) where TService : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(extensionOptions);
        return builder.AddServiceOptions<TService>(options =>
        {
            var existing = options.Interceptors.FirstOrDefault(registration =>
                registration.Type == typeof(A2AGrpcExtensionInterceptor));
            if (existing is not null)
            {
                if (existing.Arguments is [A2AGrpcExtensionOptions previous] &&
                    ReferenceEquals(previous.Registry, extensionOptions.Registry) &&
                    Equals(previous.CanActivate, extensionOptions.CanActivate))
                {
                    return;
                }

                throw new InvalidOperationException("Extension negotiation is already configured with a different registry or policy for this gRPC service.");
            }

            options.Interceptors.Add<A2AGrpcExtensionInterceptor>(extensionOptions);
        });
    }

    /// <summary>
    /// Adds the generic gRPC custom operation service and its immutable operation registry.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="registry">The custom operation registry used by the gRPC envelope.</param>
    /// <returns>An <see cref="IGrpcServerBuilder"/> for further gRPC configuration.</returns>
    /// <remarks>
    /// Negotiation is scoped to the generic custom operation service. To opt the standard
    /// A2A service into negotiation as well, use AddA2AGrpc with explicit extension options.
    /// </remarks>
    public static IGrpcServerBuilder AddA2AGrpcCustomOperations(
        this IServiceCollection services,
        A2A.A2ACustomOperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registry);
        services.AddSingleton(registry);
        return services.AddGrpc()
            .WithA2AExtensions<A2AGrpcCustomOperationService>(new A2AGrpcExtensionOptions(registry));
    }

    /// <summary>
    /// Maps the A2A gRPC service onto the endpoint pipeline. Requires <see cref="AddA2AGrpc(IServiceCollection)"/> (or
    /// <c>AddGrpc</c>) and a registered <see cref="A2A.IA2ARequestHandler"/> in the service provider.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>A builder for configuring the gRPC service endpoints.</returns>
    public static GrpcServiceEndpointConventionBuilder MapGrpcA2A(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapGrpcService<A2AGrpcService>();
    }

    /// <summary>
    /// Maps the generic A2A gRPC custom operation envelope service.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>A builder for configuring the gRPC service endpoints.</returns>
    public static GrpcServiceEndpointConventionBuilder MapGrpcA2ACustomOperations(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapGrpcService<A2AGrpcCustomOperationService>();
    }
}

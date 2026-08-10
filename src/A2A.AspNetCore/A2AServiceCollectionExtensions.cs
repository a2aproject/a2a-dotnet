using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace A2A.AspNetCore;

/// <summary>
/// DI registration extensions for the A2A easy-path agent hosting.
/// </summary>
public static class A2AServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IAgentHandler"/> and supporting infrastructure
    /// for the easy-path agent hosting pattern. Call <see cref="A2ARouteBuilderExtensions.MapA2A(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder, string)"/>
    /// to map the endpoint.
    /// </summary>
    /// <typeparam name="THandler">The agent handler type implementing <see cref="IAgentHandler"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="agentCard">The agent card describing this agent's capabilities.</param>
    /// <param name="configureOptions">Optional callback to configure <see cref="A2AServerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddA2AAgent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services,
        AgentCard agentCard,
        Action<A2AServerOptions>? configureOptions = null)
        where THandler : class, IAgentHandler
    {
        services.AddSingleton<IAgentHandler, THandler>();
        services.AddSingleton(agentCard);

        var options = new A2AServerOptions();
        configureOptions?.Invoke(options);
        services.AddSingleton(options);

        services.TryAddSingleton<ChannelEventNotifier>();

        services.TryAddSingleton<ITaskStore, InMemoryTaskStore>();

        // Explicitly cap the request body size at 10 MB (BUG-10) instead of relying on
        // the host's framework default (Kestrel's default is ~30 MB and varies by host).
        // Configuring KestrelServerOptions is a no-op on non-Kestrel hosts; the JSON-RPC
        // processor additionally enforces the same limit per-request.
        services.Configure<KestrelServerOptions>(
            options => options.Limits.MaxRequestBodySize = A2ARequestLimits.MaxRequestBodySize);

        services.TryAddSingleton<IA2ARequestHandler>(sp =>
            new A2AServer(
                sp.GetRequiredService<IAgentHandler>(),
                sp.GetRequiredService<ITaskStore>(),
                sp.GetRequiredService<ChannelEventNotifier>(),
                sp.GetRequiredService<ILogger<A2AServer>>(),
                sp.GetRequiredService<A2AServerOptions>()));

        return services;
    }
}

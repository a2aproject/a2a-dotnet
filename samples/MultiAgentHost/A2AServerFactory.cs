using System.Collections.Concurrent;
using A2A;
using Microsoft.Extensions.Logging;

namespace MultiAgentHost;

/// <summary>
/// Describes an agent hosted on the platform, identified by its subdomain.
/// </summary>
public sealed record AgentRegistration(string Subdomain, string AgentName, string Description, AgentSkill[] Skills, IAgentHandler Handler);

/// <summary>
/// Factory that creates and caches <see cref="A2AServer"/> instances per subdomain.
/// Each agent gets its own <see cref="InMemoryTaskStore"/> and <see cref="ChannelEventNotifier"/>,
/// guaranteeing full task isolation between agents.
/// </summary>
public sealed class A2AServerFactory : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, AgentRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, A2AServer> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILoggerFactory _loggerFactory;

    public A2AServerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    /// <summary>Registers an agent configuration for a given subdomain.</summary>
    public void Register(AgentRegistration registration)
    {
        _registrations[registration.Subdomain] = registration;
    }

    /// <summary>Returns the registered subdomains.</summary>
    public IEnumerable<string> Subdomains => _registrations.Keys;

    /// <summary>Resolves or lazily creates the <see cref="A2AServer"/> for the given subdomain.</summary>
    public A2AServer? GetServer(string subdomain)
    {
        if (!_registrations.TryGetValue(subdomain, out var reg))
            return null;

        return _servers.GetOrAdd(subdomain, _ =>
        {
            var store = new InMemoryTaskStore();
            var notifier = new ChannelEventNotifier();
            var logger = _loggerFactory.CreateLogger<A2AServer>();
            return new A2AServer(reg.Handler, store, notifier, logger);
        });
    }

    /// <summary>Builds an <see cref="AgentCard"/> for the given subdomain and base URL.</summary>
    public AgentCard? GetAgentCard(string subdomain, string baseUrl)
    {
        if (!_registrations.TryGetValue(subdomain, out var reg))
            return null;

        return new AgentCard
        {
            Name = reg.AgentName,
            Description = reg.Description,
            Version = "1.0.0",
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    Url = baseUrl,
                    ProtocolBinding = "JSONRPC",
                    ProtocolVersion = "1.0",
                }
            ],
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["text/plain"],
            Capabilities = new AgentCapabilities
            {
                Streaming = true,
                PushNotifications = false,
            },
            Skills = [.. reg.Skills],
        };
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var server in _servers.Values)
            await server.DisposeAsync();
    }
}

using System.Collections.Concurrent;

namespace A2A;

/// <summary>
/// Factory for creating <see cref="IA2AClient"/> instances from an <see cref="AgentCard"/>.
/// Selects the best protocol binding based on the agent's supported interfaces and the caller's preferences.
/// </summary>
/// <remarks>
/// The factory ships with built-in support for <see cref="ProtocolBindingNames.HttpJson"/> and
/// <see cref="ProtocolBindingNames.JsonRpc"/>. Additional bindings (including
/// <see cref="ProtocolBindingNames.Grpc"/> and custom bindings) can be registered via
/// <see cref="Register"/>.
/// </remarks>
public static class A2AClientFactory
{
    private static readonly ConcurrentDictionary<string, Func<Uri, HttpClient?, IA2AClient>> s_bindings = new(StringComparer.OrdinalIgnoreCase)
    {
        [ProtocolBindingNames.HttpJson] = (url, httpClient) => new A2AHttpJsonClient(url, httpClient),
        [ProtocolBindingNames.JsonRpc] = (url, httpClient) => new A2AClient(url, httpClient),
    };

    /// <summary>
    /// Registers a custom protocol binding so the factory can create clients for it.
    /// </summary>
    /// <param name="protocolBinding">
    /// The protocol binding name (e.g. <c>"GRPC"</c>). Matching is case-insensitive.
    /// </param>
    /// <param name="clientFactory">
    /// A delegate that creates an <see cref="IA2AClient"/> given the interface URL and an optional <see cref="HttpClient"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="protocolBinding"/> or <paramref name="clientFactory"/> is <see langword="null"/>.
    /// </exception>
    public static void Register(string protocolBinding, Func<Uri, HttpClient?, IA2AClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(protocolBinding);
        ArgumentNullException.ThrowIfNull(clientFactory);
        s_bindings[protocolBinding] = clientFactory;
    }

    /// <summary>
    /// Creates an <see cref="IA2AClient"/> from an <see cref="AgentCard"/> by selecting the
    /// best matching protocol binding from the card's <see cref="AgentCard.SupportedInterfaces"/>.
    /// </summary>
    /// <param name="agentCard">The agent card describing the agent's supported interfaces.</param>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    /// <param name="options">
    /// Optional client options controlling binding preference.
    /// Defaults to preferring HTTP+JSON first, with JSON-RPC as fallback.
    /// </param>
    /// <returns>An <see cref="IA2AClient"/> configured for the best available protocol binding.</returns>
    /// <exception cref="A2AException">
    /// Thrown when no supported interface in the agent card matches the preferred bindings,
    /// or when a matched binding has no registered client factory.
    /// </exception>
    public static IA2AClient Create(AgentCard agentCard, HttpClient? httpClient = null, A2AClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(agentCard);

        options ??= new A2AClientOptions();

        foreach (var preferredBinding in options.PreferredBindings)
        {
            var matchingInterface = agentCard.SupportedInterfaces
                .FirstOrDefault(i => string.Equals(i.ProtocolBinding, preferredBinding, StringComparison.OrdinalIgnoreCase));

            if (matchingInterface is not null)
            {
                var url = new Uri(matchingInterface.Url);

                if (s_bindings.TryGetValue(preferredBinding, out var factory))
                {
                    return factory(url, httpClient);
                }

                throw new A2AException(
                    $"Protocol binding '{preferredBinding}' matched an agent interface but has no registered client factory. Call A2AClientFactory.Register to add one.",
                    A2AErrorCode.InvalidRequest);
            }
        }

        var available = agentCard.SupportedInterfaces.Count > 0
            ? string.Join(", ", agentCard.SupportedInterfaces.Select(i => i.ProtocolBinding))
            : "none";
        var requested = string.Join(", ", options.PreferredBindings);

        throw new A2AException(
            $"No supported interface matches the preferred protocol bindings. Requested: [{requested}]. Available: [{available}].",
            A2AErrorCode.InvalidRequest);
    }
}

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
/// <c>A2AClientFactory.Register</c>.
/// </remarks>
public static class A2AClientFactory
{
    private static readonly ConcurrentDictionary<string, Func<Uri, HttpClient?, A2AClientOperationBindings?, IA2AClient>> s_bindings = new(StringComparer.OrdinalIgnoreCase)
    {
        [ProtocolBindingNames.HttpJson] = (url, httpClient, operationBindings) =>
            operationBindings is null
                ? new A2AHttpJsonClient(url, httpClient)
                : new A2AHttpJsonClient(
                    url,
                    operationBindings,
                    httpClient ?? A2AClient.s_sharedClient),
        [ProtocolBindingNames.JsonRpc] = (url, httpClient, operationBindings) =>
            operationBindings is null
                ? new A2AClient(url, httpClient)
                : new A2AClient(
                    url,
                    operationBindings,
                    httpClient ?? A2AClient.s_sharedClient),
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
        s_bindings[protocolBinding] =
            (url, httpClient, _) => clientFactory(url, httpClient);
    }

    /// <summary>
    /// Registers a custom protocol binding that supports custom operation bindings.
    /// </summary>
    /// <param name="protocolBinding">
    /// The protocol binding name. Matching is case-insensitive.
    /// </param>
    /// <param name="clientFactory">
    /// A delegate that creates an <see cref="A2A.IA2AClient"/> from the interface URL,
    /// optional <see cref="System.Net.Http.HttpClient"/>, and optional
    /// <see cref="A2A.A2AClientOperationBindings"/>.
    /// </param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="protocolBinding"/> or <paramref name="clientFactory"/> is
    /// <see langword="null"/>.
    /// </exception>
    public static void RegisterWithOperationBindings(
        string protocolBinding,
        Func<Uri, HttpClient?, A2AClientOperationBindings?, IA2AClient> clientFactory)
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
    /// <remarks>
    /// Selection follows spec Section 8.3: the agent's <see cref="AgentCard.SupportedInterfaces"/>
    /// order is respected (first entry is preferred), filtered to bindings listed in
    /// <see cref="A2AClientOptions.PreferredBindings"/>. This means the agent's preference
    /// wins when multiple bindings are mutually supported.
    /// </remarks>
    public static IA2AClient Create(AgentCard agentCard, HttpClient? httpClient = null, A2AClientOptions? options = null)
        => CreateCore(
            agentCard,
            httpClient,
            options,
            operationBindings: null);

    /// <summary>
    /// Creates an <see cref="IA2AClient"/> with custom operation bindings.
    /// </summary>
    /// <param name="agentCard">The agent card describing the supported interfaces.</param>
    /// <param name="operationBindings">The custom operation bindings.</param>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    /// <param name="options">Optional protocol binding preferences.</param>
    /// <returns>A client configured for the selected protocol binding.</returns>
    public static IA2AClient Create(
        AgentCard agentCard,
        A2AClientOperationBindings operationBindings,
        HttpClient? httpClient = null,
        A2AClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(operationBindings);
        return CreateCore(
            agentCard,
            httpClient,
            options,
            operationBindings);
    }

    private static IA2AClient CreateCore(
        AgentCard agentCard,
        HttpClient? httpClient,
        A2AClientOptions? options,
        A2AClientOperationBindings? operationBindings)
    {
        ArgumentNullException.ThrowIfNull(agentCard);

        options ??= new A2AClientOptions();
        var preferredSet = new HashSet<string>(options.PreferredBindings, StringComparer.OrdinalIgnoreCase);

        // Walk agent's interfaces in declared preference order (spec Section 8.3.1),
        // selecting the first one the client also supports.
        foreach (var agentInterface in agentCard.SupportedInterfaces)
        {
            if (!preferredSet.Contains(agentInterface.ProtocolBinding))
            {
                continue;
            }

            var url = new Uri(agentInterface.Url);

            if (s_bindings.TryGetValue(agentInterface.ProtocolBinding, out var factory))
            {
                return factory(url, httpClient, operationBindings);
            }

            throw new A2AException(
                $"Protocol binding '{agentInterface.ProtocolBinding}' matched an agent interface but has no registered client factory. Call A2AClientFactory.Register to add one.",
                A2AErrorCode.InvalidRequest);
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

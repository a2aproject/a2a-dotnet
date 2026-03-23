using System.Collections.Concurrent;
using System.Text.Json;

namespace A2A;

/// <summary>
/// Factory for creating <see cref="IA2AClient"/> instances.
/// Supports creating clients from a parsed <see cref="AgentCard"/>, a raw agent card JSON string,
/// or by fetching the agent card from a well-known URL. When given raw JSON, the factory detects
/// the protocol version and delegates to a registered fallback for non-v1.0 agent cards.
/// </summary>
/// <remarks>
/// The factory ships with built-in support for <see cref="ProtocolBindingNames.HttpJson"/> and
/// <see cref="ProtocolBindingNames.JsonRpc"/>. Additional bindings (including
/// <see cref="ProtocolBindingNames.Grpc"/> and custom bindings) can be registered via
/// <see cref="Register"/>. To support older protocol versions (e.g. v0.3), register a fallback
/// via <see cref="RegisterFallback"/> or set <see cref="A2AClientOptions.FallbackFactory"/>.
/// </remarks>
public static class A2AClientFactory
{
    private static readonly ConcurrentDictionary<string, Func<Uri, HttpClient?, IA2AClient>> s_bindings = new(StringComparer.OrdinalIgnoreCase)
    {
        [ProtocolBindingNames.HttpJson] = (url, httpClient) => new A2AHttpJsonClient(url, httpClient),
        [ProtocolBindingNames.JsonRpc] = (url, httpClient) => new A2AClient(url, httpClient),
    };

    private static Func<string, Uri, HttpClient?, IA2AClient>? s_fallbackFactory;

    private static readonly HttpClient s_sharedClient = new();

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
    /// Registers a global fallback factory for agent cards that do not declare
    /// <c>supportedInterfaces</c> (e.g. v0.3 agents). This can be overridden per-call
    /// via <see cref="A2AClientOptions.FallbackFactory"/>.
    /// </summary>
    /// <param name="fallbackFactory">
    /// A delegate that creates an <see cref="IA2AClient"/> from the raw agent card JSON,
    /// the agent's base URL, and an optional <see cref="HttpClient"/>.
    /// </param>
    public static void RegisterFallback(Func<string, Uri, HttpClient?, IA2AClient> fallbackFactory)
    {
        ArgumentNullException.ThrowIfNull(fallbackFactory);
        s_fallbackFactory = fallbackFactory;
    }

    /// <summary>
    /// Clears any previously registered global fallback factory.
    /// </summary>
    public static void ClearFallback() => s_fallbackFactory = null;

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
                return factory(url, httpClient);
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

    /// <summary>
    /// Creates an <see cref="IA2AClient"/> from a raw agent card JSON string. Detects the
    /// protocol version from the card structure: cards with <c>supportedInterfaces</c> are
    /// treated as v1.0 and routed through <see cref="Create(AgentCard, HttpClient?, A2AClientOptions?)"/>;
    /// all other cards are passed to a registered fallback factory.
    /// </summary>
    /// <param name="agentCardJson">The raw JSON string of the agent card.</param>
    /// <param name="baseUrl">The base URL of the agent's hosting service, used as fallback when the card does not specify a URL.</param>
    /// <param name="httpClient">Optional HTTP client to use for requests.</param>
    /// <param name="options">Optional client options controlling binding preference and fallback behavior.</param>
    /// <returns>An <see cref="IA2AClient"/> configured for the appropriate protocol version and binding.</returns>
    /// <exception cref="A2AException">
    /// Thrown when the card does not declare <c>supportedInterfaces</c> and no fallback factory is available.
    /// </exception>
    public static IA2AClient Create(string agentCardJson, Uri baseUrl, HttpClient? httpClient = null, A2AClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(agentCardJson);
        ArgumentNullException.ThrowIfNull(baseUrl);

        using var doc = JsonDocument.Parse(agentCardJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("supportedInterfaces", out var interfaces) &&
            interfaces.ValueKind == JsonValueKind.Array &&
            interfaces.GetArrayLength() > 0)
        {
            var agentCard = BuildAgentCard(root, interfaces, baseUrl);
            return Create(agentCard, httpClient, options);
        }

        var fallback = options?.FallbackFactory ?? s_fallbackFactory;
        if (fallback is not null)
        {
            return fallback(agentCardJson, baseUrl, httpClient);
        }

        throw new A2AException(
            "Agent card does not declare supportedInterfaces and no fallback factory is registered. " +
            "To support older protocol versions, register a fallback with A2AClientFactory.RegisterFallback.",
            A2AErrorCode.InvalidRequest);
    }

    /// <summary>
    /// Fetches the agent card from the well-known URL and creates an appropriate <see cref="IA2AClient"/>.
    /// </summary>
    /// <param name="baseUrl">The base URL of the agent's hosting service.</param>
    /// <param name="httpClient">Optional HTTP client to use for requests.</param>
    /// <param name="options">Optional client options controlling the agent card path, binding preference, and fallback behavior.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An <see cref="IA2AClient"/> configured for the appropriate protocol version and binding.</returns>
    public static async Task<IA2AClient> CreateAsync(
        Uri baseUrl,
        HttpClient? httpClient = null,
        A2AClientOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);

        options ??= new A2AClientOptions();
        var http = httpClient ?? s_sharedClient;

        var cardUri = new Uri(baseUrl, options.AgentCardPath);

        using var request = new HttpRequestMessage(HttpMethod.Get, cardUri);
        request.Headers.TryAddWithoutValidation("A2A-Version", "1.0");

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return Create(json, baseUrl, httpClient, options);
    }

    private static AgentCard BuildAgentCard(JsonElement root, JsonElement interfaces, Uri baseUrl)
    {
        var agentCard = new AgentCard
        {
            Name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
            Description = root.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : "",
            SupportedInterfaces = [],
        };

        foreach (var iface in interfaces.EnumerateArray())
        {
            agentCard.SupportedInterfaces.Add(new AgentInterface
            {
                Url = iface.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? baseUrl.ToString() : baseUrl.ToString(),
                ProtocolBinding = iface.TryGetProperty("protocolBinding", out var bindingProp) ? bindingProp.GetString() ?? "" : "",
                ProtocolVersion = iface.TryGetProperty("protocolVersion", out var verProp) ? verProp.GetString() ?? "" : "",
            });
        }

        return agentCard;
    }
}

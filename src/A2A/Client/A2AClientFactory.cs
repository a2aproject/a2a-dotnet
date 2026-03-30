using System.Collections.Concurrent;
using System.Text.Json;

namespace A2A;

/// <summary>
/// Factory for creating <see cref="IA2AClient"/> instances.
/// Supports creating clients from a parsed <see cref="AgentCard"/>, a raw agent card JSON string,
/// or by fetching the agent card from a well-known URL. When given raw JSON, the factory detects
/// the protocol version and delegates to the appropriate registered binding.
/// </summary>
/// <remarks>
/// The factory ships with built-in support for v1.0 <see cref="ProtocolBindingNames.HttpJson"/> and
/// <see cref="ProtocolBindingNames.JsonRpc"/>. Additional bindings, protocol versions (e.g. v0.3),
/// and custom bindings can be registered via <see cref="Register(string, string, Func{Uri, HttpClient, IA2AClient})"/>.
/// </remarks>
public static class A2AClientFactory
{
    private static readonly ConcurrentDictionary<BindingKey, Func<Uri, HttpClient?, IA2AClient>> s_bindings = new()
    {
        [new(ProtocolBindingNames.HttpJson, "1.0")] = (url, httpClient) => new A2AHttpJsonClient(url, httpClient),
        [new(ProtocolBindingNames.JsonRpc, "1.0")] = (url, httpClient) => new A2AClient(url, httpClient),
    };

    private static readonly HttpClient s_sharedClient = new();

    /// <summary>
    /// Registers a client factory for a specific protocol binding and version.
    /// </summary>
    /// <param name="protocolBinding">
    /// The protocol binding name (e.g. <c>"JSONRPC"</c>, <c>"HTTP+JSON"</c>, <c>"GRPC"</c>).
    /// Matching is case-insensitive.
    /// </param>
    /// <param name="protocolVersion">
    /// The protocol version (e.g. <c>"1.0"</c>, <c>"0.3"</c>). Use major.minor format.
    /// </param>
    /// <param name="clientFactory">
    /// A delegate that creates an <see cref="IA2AClient"/> given the interface URL and an optional <see cref="HttpClient"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="protocolBinding"/>, <paramref name="protocolVersion"/>,
    /// or <paramref name="clientFactory"/> is <see langword="null"/>.
    /// </exception>
    public static void Register(string protocolBinding, string protocolVersion, Func<Uri, HttpClient?, IA2AClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(protocolBinding);
        ArgumentNullException.ThrowIfNull(protocolVersion);
        ArgumentNullException.ThrowIfNull(clientFactory);
        s_bindings[new(protocolBinding, protocolVersion)] = clientFactory;
    }

    /// <summary>
    /// Registers a client factory for a specific protocol binding at version 1.0.
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
        => Register(protocolBinding, "1.0", clientFactory);

    /// <summary>
    /// Removes a previously registered client factory for a specific protocol binding and version.
    /// </summary>
    /// <param name="protocolBinding">The protocol binding name.</param>
    /// <param name="protocolVersion">The protocol version.</param>
    /// <returns><see langword="true"/> if the binding was removed; <see langword="false"/> if it was not found.</returns>
    public static bool Unregister(string protocolBinding, string protocolVersion)
    {
        ArgumentNullException.ThrowIfNull(protocolBinding);
        ArgumentNullException.ThrowIfNull(protocolVersion);
        return s_bindings.TryRemove(new(protocolBinding, protocolVersion), out _);
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
            var version = NormalizeMajorMinor(agentInterface.ProtocolVersion);
            var key = new BindingKey(agentInterface.ProtocolBinding, version);

            if (s_bindings.TryGetValue(key, out var factory))
            {
                return factory(url, httpClient);
            }

            throw new A2AException(
                $"Protocol binding '{agentInterface.ProtocolBinding}' version '{version}' matched an agent interface but has no registered client factory. " +
                $"Call A2AClientFactory.Register(\"{agentInterface.ProtocolBinding}\", \"{version}\", factory) to add one.",
                A2AErrorCode.InvalidRequest);
        }

        var available = agentCard.SupportedInterfaces.Count > 0
            ? string.Join(", ", agentCard.SupportedInterfaces.Select(i => $"{i.ProtocolBinding}/{i.ProtocolVersion}"))
            : "none";
        var requested = string.Join(", ", options.PreferredBindings);

        throw new A2AException(
            $"No supported interface matches the preferred protocol bindings. Requested: [{requested}]. Available: [{available}].",
            A2AErrorCode.InvalidRequest);
    }

    /// <summary>
    /// Creates an <see cref="IA2AClient"/> from a raw agent card JSON string. Detects the
    /// protocol version from the card structure: cards with <c>supportedInterfaces</c> are
    /// parsed and routed through <see cref="Create(AgentCard, HttpClient?, A2AClientOptions?)"/>;
    /// cards without <c>supportedInterfaces</c> (e.g. v0.3) are handled by synthesizing an
    /// interface from the card's <c>url</c> and <c>protocolVersion</c> fields.
    /// </summary>
    /// <param name="agentCardJson">The raw JSON string of the agent card.</param>
    /// <param name="baseUrl">The base URL of the agent's hosting service, used as fallback when the card does not specify a URL.</param>
    /// <param name="httpClient">Optional HTTP client to use for requests.</param>
    /// <param name="options">Optional client options controlling binding preference.</param>
    /// <returns>An <see cref="IA2AClient"/> configured for the appropriate protocol version and binding.</returns>
    /// <exception cref="A2AException">
    /// Thrown when no matching binding+version is registered for the agent card.
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

        // Legacy card without supportedInterfaces — synthesize an interface
        // from the card's url, protocolVersion, and preferredTransport fields.
        var agentUrl = (root.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
            ? urlProp.GetString() : null) ?? baseUrl.ToString();

        var version = root.TryGetProperty("protocolVersion", out var verProp) && verProp.ValueKind == JsonValueKind.String
            ? NormalizeMajorMinor(verProp.GetString() ?? "0.3")
            : "0.3";

        var binding = root.TryGetProperty("preferredTransport", out var transportProp) && transportProp.ValueKind == JsonValueKind.String
            ? MapLegacyTransport(transportProp.GetString())
            : ProtocolBindingNames.JsonRpc;

        var legacyCard = new AgentCard
        {
            Name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
            Description = root.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : "",
            SupportedInterfaces = [new AgentInterface { Url = agentUrl, ProtocolBinding = binding, ProtocolVersion = version }],
        };

        return Create(legacyCard, httpClient, options ?? new A2AClientOptions { PreferredBindings = [binding] });
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

    /// <summary>
    /// Normalizes a version string to major.minor format (e.g. "0.3.0" → "0.3", "1.0" → "1.0").
    /// </summary>
    /// <param name="version">The version string to normalize, or <see langword="null"/>.</param>
    private static string NormalizeMajorMinor(string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return string.Empty;
        }

        var dotIndex = version.IndexOf('.');
        if (dotIndex < 0)
        {
            return version;
        }

        var secondDot = version.IndexOf('.', dotIndex + 1);
        return secondDot < 0 ? version : version.Substring(0, secondDot);
    }

    /// <summary>
    /// Maps legacy v0.3 <c>preferredTransport</c> values to protocol binding names.
    /// </summary>
    /// <param name="transport">The legacy transport name to map.</param>
    private static string MapLegacyTransport(string? transport) =>
        transport?.ToUpperInvariant() switch
        {
            "JSONRPC" => ProtocolBindingNames.JsonRpc,
            "SSE" => ProtocolBindingNames.JsonRpc, // v0.3 SSE used JSON-RPC underneath
            _ => ProtocolBindingNames.JsonRpc,
        };

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
                Url = (iface.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
                    ? urlProp.GetString() : null) ?? baseUrl.ToString(),
                ProtocolBinding = iface.TryGetProperty("protocolBinding", out var bindingProp) ? bindingProp.GetString() ?? "" : "",
                ProtocolVersion = iface.TryGetProperty("protocolVersion", out var verProp) ? verProp.GetString() ?? "" : "",
            });
        }

        return agentCard;
    }

    /// <summary>Composite key for binding+version lookup with case-insensitive binding matching.</summary>
    /// <param name="Binding">The protocol binding name.</param>
    /// <param name="Version">The protocol version.</param>
    private readonly record struct BindingKey(string Binding, string Version) : IEquatable<BindingKey>
    {
        public bool Equals(BindingKey other) =>
            string.Equals(Binding, other.Binding, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Version, other.Version, StringComparison.Ordinal);

        public override int GetHashCode() =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Binding), StringComparer.Ordinal.GetHashCode(Version));
    }
}

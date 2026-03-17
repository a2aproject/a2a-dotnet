namespace A2A.V0_3Compat;

using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using V03 = A2A.V0_3;

/// <summary>
/// Factory that creates an <see cref="A2A.IA2AClient"/> by inspecting an agent card
/// to detect the protocol version. For v1.0 agents, delegates to
/// <see cref="A2A.A2AClientFactory"/> for protocol binding selection. For v0.3 agents,
/// returns a <see cref="V03ClientAdapter"/> wrapping the v0.3 client.
/// </summary>
public static class VersionNegotiatingClientFactory
{
    private static readonly HttpClient s_sharedClient = new();

    /// <summary>Creates an <see cref="A2A.IA2AClient"/> for the given base URL by fetching the agent card and selecting the best protocol version.</summary>
    /// <param name="baseUrl">The base URL of the agent's hosting service.</param>
    /// <param name="httpClient">Optional HTTP client to use for requests.</param>
    /// <param name="options">Optional version negotiation options.</param>
    /// <param name="clientOptions">Optional client options for v1.0 protocol binding selection.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An <see cref="A2A.IA2AClient"/> configured for the appropriate protocol version.</returns>
    public static async Task<A2A.IA2AClient> CreateAsync(
        Uri baseUrl,
        HttpClient? httpClient = null,
        VersionNegotiationOptions? options = null,
        A2A.A2AClientOptions? clientOptions = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new VersionNegotiationOptions();
        logger ??= NullLogger.Instance;
        var http = httpClient ?? s_sharedClient;

        var cardUri = new Uri(baseUrl, options.AgentCardPath);

        using var request = new HttpRequestMessage(HttpMethod.Get, cardUri);
        request.Headers.TryAddWithoutValidation("A2A-Version", "1.0");

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return CreateFromAgentCard(json, baseUrl, httpClient, options, clientOptions, logger);
    }

    /// <summary>Creates an <see cref="A2A.IA2AClient"/> from a pre-fetched agent card JSON string, detecting the version from the card structure.</summary>
    /// <param name="agentCardJson">The agent card JSON string.</param>
    /// <param name="baseUrl">The base URL of the agent's hosting service.</param>
    /// <param name="httpClient">Optional HTTP client to use for requests.</param>
    /// <param name="options">Optional version negotiation options.</param>
    /// <param name="clientOptions">Optional client options for v1.0 protocol binding selection.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <returns>An <see cref="A2A.IA2AClient"/> configured for the appropriate protocol version.</returns>
    public static A2A.IA2AClient CreateFromAgentCard(
        string agentCardJson,
        Uri baseUrl,
        HttpClient? httpClient = null,
        VersionNegotiationOptions? options = null,
        A2A.A2AClientOptions? clientOptions = null,
        ILogger? logger = null)
    {
        options ??= new VersionNegotiationOptions();
        logger ??= NullLogger.Instance;

        using var doc = JsonDocument.Parse(agentCardJson);
        var root = doc.RootElement;

        // Detect v1.0: has supportedInterfaces array with at least one element
        if (root.TryGetProperty("supportedInterfaces", out var interfaces) &&
            interfaces.ValueKind == JsonValueKind.Array &&
            interfaces.GetArrayLength() > 0)
        {
            // Build an AgentCard with the supported interfaces for the binding factory
            var agentCard = new A2A.AgentCard
            {
                Name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
                Description = root.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : "",
                SupportedInterfaces = [],
            };

            foreach (var iface in interfaces.EnumerateArray())
            {
                var agentInterface = new A2A.AgentInterface
                {
                    Url = iface.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? baseUrl.ToString() : baseUrl.ToString(),
                    ProtocolBinding = iface.TryGetProperty("protocolBinding", out var bindingProp) ? bindingProp.GetString() ?? "" : "",
                    ProtocolVersion = iface.TryGetProperty("protocolVersion", out var verProp) ? verProp.GetString() ?? "" : "",
                };
                agentCard.SupportedInterfaces.Add(agentInterface);
            }

            LogDetectedVersion(logger, "1.0", agentCard.SupportedInterfaces[0].Url);
            return A2A.A2AClientFactory.Create(agentCard, httpClient, clientOptions);
        }

        // Try v0.3 fallback
        if (!options.AllowV03Fallback)
        {
            throw new InvalidOperationException(
                "Agent card does not indicate v1.0 support (no supportedInterfaces) and v0.3 fallback is disabled.");
        }

        // v0.3 cards have a "url" field at the root level
        string v03Url;
        if (root.TryGetProperty("url", out var v03UrlProp) &&
            v03UrlProp.ValueKind == JsonValueKind.String)
        {
            v03Url = v03UrlProp.GetString()!;
        }
        else
        {
            v03Url = baseUrl.ToString();
        }

        LogDetectedVersion(logger, "0.3", v03Url);
        var v03Client = new V03.A2AClient(new Uri(v03Url), httpClient);
        return new V03ClientAdapter(v03Client);
    }

    private static void LogDetectedVersion(ILogger logger, string version, string url)
    {
        logger.DetectedAgentCardVersion(version, url);
    }
}

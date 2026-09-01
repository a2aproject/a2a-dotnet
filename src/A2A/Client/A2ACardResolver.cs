using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace A2A;

/// <summary>
/// Resolves Agent Card information from an A2A-compatible endpoint.
/// </summary>
public sealed class A2ACardResolver
{
    private readonly HttpClient _httpClient;
    private readonly Uri _agentCardPath;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="A2ACardResolver"/>.
    /// </summary>
    /// <param name="baseUrl">The base url of the agent's hosting service.</param>
    /// <param name="httpClient">Optional HTTP client (if not provided, a shared one will be used).</param>
    /// <param name="agentCardPath">Path to the agent card (defaults to "/.well-known/agent-card.json").</param>
    /// <param name="logger">Optional logger.</param>
    public A2ACardResolver(
        Uri baseUrl,
        HttpClient? httpClient = null,
        string agentCardPath = "/.well-known/agent-card.json",
        ILogger? logger = null)
    {
        if (baseUrl is null)
        {
            throw new ArgumentNullException(nameof(baseUrl), "Base URL cannot be null.");
        }

        if (string.IsNullOrEmpty(agentCardPath))
        {
            throw new ArgumentNullException(nameof(agentCardPath), "Agent card path cannot be null or empty.");
        }

        _agentCardPath = new Uri(baseUrl, agentCardPath.TrimStart('/'));

        _httpClient = httpClient ?? A2AClient.s_sharedClient;

        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Gets the agent card asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The agent card.</returns>
    public async Task<AgentCard> GetAgentCardAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = A2ADiagnostics.Source.StartActivity("A2ACardResolver.GetAgentCard", ActivityKind.Client);
        activity?.SetTag("url.full", _agentCardPath.ToString());

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.FetchingAgentCardFromUrl(_agentCardPath);
        }

        try
        {
            using var response = await _httpClient.GetAsync(_agentCardPath, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            // Buffer the response so we can attempt multiple deserialization strategies
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return JsonSerializer.Deserialize(bytes, A2AJsonUtilities.JsonContext.Default.AgentCard)
                    ?? throw new A2AException("Failed to parse agent card JSON.");
            }
            catch (JsonException ex)
            {
                // v1.0 deserialization failed — attempt v0.3 upcast
                _logger.AttemptingV03AgentCardUpcast(ex);
                return UpcastV03AgentCard(bytes)
                    ?? throw new A2AException($"Failed to parse JSON: {ex.Message}");
            }
        }
        catch (JsonException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.FailedToParseAgentCardJson(ex);
            throw new A2AException($"Failed to parse JSON: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            HttpStatusCode statusCode = ex.StatusCode ?? HttpStatusCode.InternalServerError;

            _logger.HttpRequestFailedWithStatusCode(ex, statusCode);
            throw new A2AException("HTTP request failed", ex);
        }
    }

    /// <summary>
    /// Attempts to parse a v0.3 agent card and upcast it to a v1.0 <see cref="AgentCard"/>.
    /// A v0.3 card has a top-level "url" and optional "preferredTransport" instead of "supportedInterfaces".
    /// </summary>
    /// <param name="bytes">The raw JSON bytes of the agent card response.</param>
    /// <returns>An upcast v1.0 <see cref="AgentCard"/> if the JSON is a valid v0.3 card; otherwise <c>null</c>.</returns>
    private static AgentCard? UpcastV03AgentCard(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // v0.3 cards MUST have a "url" property
        if (!root.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var url = urlElement.GetString();
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        // v0.3 cards MUST have a top-level "protocolVersion" — use as a discriminator since we only reach here after v1.0 deserialization failed
        if (!root.TryGetProperty("protocolVersion", out var pvElement) || pvElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var protocolVersion = pvElement.GetString() ?? "0.3";

        // Determine the protocol binding from preferredTransport (defaults to JSONRPC)
        var protocolBinding = ProtocolBindingNames.JsonRpc;
        if (root.TryGetProperty("preferredTransport", out var transportElement))
        {
            var transport = ExtractTransportName(transportElement);
            if (!string.IsNullOrEmpty(transport))
            {
                protocolBinding = MapV03TransportToBinding(transport!);
            }
        }

        // Build the supportedInterfaces list from the v0.3 url + preferredTransport
        var interfaces = new List<AgentInterface>
        {
            new()
            {
                ProtocolBinding = protocolBinding,
                Url = url,
                ProtocolVersion = protocolVersion,
            }
        };

        // Also include additionalInterfaces if present.
        // v0.3 entries have shape { "transport": "...", "url": "..." }, which does not
        // match v1's AgentInterface shape { "url", "protocolBinding", "protocolVersion" }.
        // Map explicitly so HTTP+JSON / GRPC bindings are preserved rather than silently
        // defaulted to JSONRPC by the v1 deserializer.
        if (root.TryGetProperty("additionalInterfaces", out var addlInterfaces) && addlInterfaces.ValueKind == JsonValueKind.Array)
        {
            foreach (var iface in addlInterfaces.EnumerateArray())
            {
                if (iface.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!iface.TryGetProperty("url", out var ifaceUrlElement) || ifaceUrlElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var ifaceUrl = ifaceUrlElement.GetString();
                if (string.IsNullOrEmpty(ifaceUrl))
                {
                    continue;
                }

                var ifaceBinding = ProtocolBindingNames.JsonRpc;
                if (iface.TryGetProperty("transport", out var ifaceTransportElement))
                {
                    var ifaceTransport = ExtractTransportName(ifaceTransportElement);
                    if (!string.IsNullOrEmpty(ifaceTransport))
                    {
                        ifaceBinding = MapV03TransportToBinding(ifaceTransport!);
                    }
                }

                interfaces.Add(new AgentInterface
                {
                    Url = ifaceUrl!,
                    ProtocolBinding = ifaceBinding,
                    ProtocolVersion = protocolVersion,
                });
            }
        }

        // Extract common fields
        var card = new AgentCard
        {
            Name = root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? "" : "",
            Description = root.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String ? desc.GetString() ?? "" : "",
            Version = root.TryGetProperty("version", out var ver) && ver.ValueKind == JsonValueKind.String ? ver.GetString() ?? "0.3" : "0.3",
            SupportedInterfaces = interfaces,
            Capabilities = new AgentCapabilities(),
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["text/plain"],
            Skills = [],
        };

        // Parse capabilities
        if (root.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Object)
        {
            if (caps.TryGetProperty("streaming", out var streaming))
                card.Capabilities.Streaming = streaming.ValueKind == JsonValueKind.True;
            if (caps.TryGetProperty("pushNotifications", out var push))
                card.Capabilities.PushNotifications = push.ValueKind == JsonValueKind.True;
        }

        // Parse default modes if present
        if (root.TryGetProperty("defaultInputModes", out var inputModes) && inputModes.ValueKind == JsonValueKind.Array)
        {
            card.DefaultInputModes = inputModes.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
        }

        if (root.TryGetProperty("defaultOutputModes", out var outputModes) && outputModes.ValueKind == JsonValueKind.Array)
        {
            card.DefaultOutputModes = outputModes.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
        }

        // Parse skills if present
        if (root.TryGetProperty("skills", out var skills) && skills.ValueKind == JsonValueKind.Array)
        {
            foreach (var skillElement in skills.EnumerateArray())
            {
                var skill = JsonSerializer.Deserialize(skillElement.GetRawText(), A2AJsonUtilities.JsonContext.Default.AgentSkill);
                if (skill is not null)
                {
                    card.Skills.Add(skill);
                }
            }
        }

        if (root.TryGetProperty("documentationUrl", out var docUrl) && docUrl.ValueKind == JsonValueKind.String)
            card.DocumentationUrl = docUrl.GetString();
        if (root.TryGetProperty("iconUrl", out var iconUrl) && iconUrl.ValueKind == JsonValueKind.String)
            card.IconUrl = iconUrl.GetString();

        return card;
    }

    /// <summary>
    /// Extracts a transport name from a v0.3 transport JSON element. v0.3 uses a string
    /// (e.g. "JSONRPC"), but some producers wrap it as { "value": "JSONRPC" }.
    /// </summary>
    /// <param name="element">The JSON element to inspect.</param>
    /// <returns>The transport name, or <c>null</c> if none could be extracted.</returns>
    private static string? ExtractTransportName(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : element.ValueKind == JsonValueKind.Object
              && element.TryGetProperty("value", out var val)
              && val.ValueKind == JsonValueKind.String
                ? val.GetString()
                : null;

    /// <summary>
    /// Maps a v0.3 transport name to a v1 protocol binding name.
    /// Unknown values are passed through so custom bindings still round-trip.
    /// </summary>
    /// <param name="transport">The v0.3 transport name.</param>
    /// <returns>The corresponding v1 protocol binding name.</returns>
    private static string MapV03TransportToBinding(string transport) =>
        transport.ToUpperInvariant() switch
        {
            "JSONRPC" or "JSON-RPC" => ProtocolBindingNames.JsonRpc,
            "HTTP+JSON" or "HTTP_JSON" or "REST" => ProtocolBindingNames.HttpJson,
            "GRPC" => ProtocolBindingNames.Grpc,
            _ => transport,
        };
}

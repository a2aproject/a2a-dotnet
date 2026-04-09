namespace A2A.V0_3Compat;

using System.Text.Json;
using V03 = A2A.V0_3;

/// <summary>
/// Factory for creating <see cref="IA2AClient"/> instances that talk to A2A v0.3 agents.
/// Use this instead of <see cref="A2AClientFactory"/> when the target agent is known to be v0.3.
/// </summary>
public static class V03CompatClientFactory
{
    private static readonly HttpClient s_sharedClient = new();

    /// <summary>
    /// Creates an <see cref="IA2AClient"/> that communicates with a v0.3 agent at the given URL.
    /// </summary>
    /// <param name="url">The agent's v0.3 JSON-RPC endpoint URL.</param>
    /// <param name="httpClient">Optional HTTP client. Uses a shared instance when <see langword="null"/>.</param>
    /// <returns>An <see cref="IA2AClient"/> backed by a v0.3 JSON-RPC client.</returns>
    public static IA2AClient Create(Uri url, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        var v03Client = new V03.A2AClient(url, httpClient);
        return new V03ClientAdapter(v03Client);
    }

    /// <summary>
    /// Creates an <see cref="IA2AClient"/> from a raw v0.3 agent card JSON string,
    /// using the <c>url</c> field in the card (or <paramref name="baseUrl"/> as fallback).
    /// </summary>
    /// <param name="agentCardJson">The raw JSON of the v0.3 agent card.</param>
    /// <param name="baseUrl">Fallback URL when the card does not specify one.</param>
    /// <param name="httpClient">Optional HTTP client. Uses a shared instance when <see langword="null"/>.</param>
    /// <returns>An <see cref="IA2AClient"/> backed by a v0.3 JSON-RPC client.</returns>
    public static IA2AClient Create(string agentCardJson, Uri baseUrl, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(agentCardJson);
        ArgumentNullException.ThrowIfNull(baseUrl);

        using var doc = JsonDocument.Parse(agentCardJson);
        var root = doc.RootElement;

        var agentUrl = root.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
            ? new Uri(urlProp.GetString() ?? baseUrl.ToString())
            : baseUrl;

        return Create(agentUrl, httpClient);
    }

    /// <summary>
    /// Fetches the agent card from the well-known URL and creates a v0.3 client.
    /// </summary>
    /// <param name="baseUrl">The base URL of the v0.3 agent's hosting service.</param>
    /// <param name="httpClient">Optional HTTP client. Uses a shared instance when <see langword="null"/>.</param>
    /// <param name="agentCardPath">Path to fetch the agent card. Defaults to <c>/.well-known/agent.json</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An <see cref="IA2AClient"/> backed by a v0.3 JSON-RPC client.</returns>
    public static async Task<IA2AClient> CreateAsync(
        Uri baseUrl,
        HttpClient? httpClient = null,
        string agentCardPath = "/.well-known/agent.json",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);

        var http = httpClient ?? s_sharedClient;
        var cardUri = new Uri(baseUrl, agentCardPath);
        var json = await http.GetStringAsync(cardUri, cancellationToken).ConfigureAwait(false);
        return Create(json, baseUrl, httpClient);
    }
}

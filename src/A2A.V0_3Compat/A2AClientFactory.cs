namespace A2A.V0_3Compat;

using System.Text.Json;

using V03 = A2A.V0_3;

/// <summary>
/// Registers a v0.3 compatibility fallback with <see cref="A2AClientFactory"/>.
/// Once registered, the factory automatically creates a v0.3 adapter for agents
/// whose agent card does not contain <c>supportedInterfaces</c>.
/// </summary>
public static class V03FallbackRegistration
{
    /// <summary>
    /// Registers the v0.3 fallback globally with <see cref="A2AClientFactory"/>.
    /// Call this once at application startup.
    /// </summary>
    public static void Register()
    {
        A2AClientFactory.RegisterFallback(CreateV03Client);
    }

    /// <summary>
    /// Creates an <see cref="IA2AClient"/> from a v0.3-shaped agent card JSON string.
    /// Can be used directly as a <see cref="A2AClientOptions.FallbackFactory"/> delegate
    /// or via <see cref="Register"/> for global registration.
    /// </summary>
    /// <param name="agentCardJson">The raw JSON string of the agent card.</param>
    /// <param name="baseUrl">The base URL of the agent, used as fallback when the card does not specify a URL.</param>
    /// <param name="httpClient">Optional HTTP client to use for requests.</param>
    /// <returns>An <see cref="IA2AClient"/> wrapping a v0.3 client.</returns>
    public static IA2AClient CreateV03Client(string agentCardJson, Uri baseUrl, HttpClient? httpClient)
    {
        using var doc = JsonDocument.Parse(agentCardJson);
        var root = doc.RootElement;

        string url;
        if (root.TryGetProperty("url", out var urlProp) &&
            urlProp.ValueKind == JsonValueKind.String)
        {
            url = urlProp.GetString()!;
        }
        else
        {
            url = baseUrl.ToString();
        }

        var v03Client = new V03.A2AClient(new Uri(url), httpClient);
        return new V03ClientAdapter(v03Client);
    }
}

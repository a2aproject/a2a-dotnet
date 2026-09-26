// Recognising a v0.3 peer, so the traversal can dial it with the compat client.
//
// The ITK instruction says which transport to use but not which protocol
// version the peer speaks, so the peer's own card is the only thing that can
// answer it. A v1.0 card lists `supportedInterfaces`; a v0.3 card has a single
// top-level `url` and no such list, which is the discriminator used here.
//
// Detection is deliberately cheap and failure-tolerant: anything unreadable is
// reported as "not v0.3" so the caller falls through to the v1.0 path it would
// have taken anyway, and a peer that is merely unreachable fails in the client
// with a real error rather than here with a misleading one.

using System.Text.Json;

namespace A2A.Itk;

/// <summary>Detects v0.3 peers from their agent card.</summary>
public static class ItkV03
{
    /// <summary>
    /// Card paths to try, in order. v0.3 standardised on <c>agent.json</c>, but the ITK
    /// agents grafted onto v0.3 releases by the <c>+itk</c> overlay tags serve the v1.0
    /// <c>agent-card.json</c> name, so both have to be attempted.
    /// </summary>
    private static readonly string[] CardPaths =
    [
        ".well-known/agent-card.json",
        ".well-known/agent.json",
    ];

    /// <summary>
    /// The peer's v0.3 JSON-RPC endpoint, or null when the peer is not v0.3.
    /// </summary>
    public static async Task<Uri?> PeerEndpointAsync(
        string agentCardUri, HttpClient httpClient, CancellationToken cancellationToken)
    {
        var baseUri = new Uri(agentCardUri.EndsWith('/') ? agentCardUri : agentCardUri + "/");

        foreach (var path in CardPaths)
        {
            var card = await ReadCardAsync(new Uri(baseUri, path), httpClient, cancellationToken);
            if (card is null)
            {
                continue;
            }

            // Asked for explicitly: a v1.0 agent may also advertise a v0.3 interface
            // (a2a-go and a2a-python both do), and that endpoint is a different URL
            // from the v1.0 one. Preferring it would route every peer through the
            // compat client, so this only fires when there is no v1.0 interface.
            if (HasV10Interface(card.Value))
            {
                return null;
            }

            if (card.Value.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String
                && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var endpoint))
            {
                return endpoint;
            }
        }

        return null;
    }

    private static bool HasV10Interface(JsonElement card)
    {
        if (!card.TryGetProperty("supportedInterfaces", out var interfaces)
            || interfaces.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var entry in interfaces.EnumerateArray())
        {
            // An entry with no protocolVersion is v1.0 by construction: the field was
            // introduced with supportedInterfaces, which v0.3 cards do not carry.
            if (!entry.TryGetProperty("protocolVersion", out var version)
                || version.ValueKind != JsonValueKind.String
                || version.GetString() != "0.3")
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<JsonElement?> ReadCardAsync(
        Uri uri, HttpClient httpClient, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }
}

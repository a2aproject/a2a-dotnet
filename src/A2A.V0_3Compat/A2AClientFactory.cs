namespace A2A.V0_3Compat;

using V03 = A2A.V0_3;

/// <summary>
/// Registers v0.3 protocol support with <see cref="A2AClientFactory"/>.
/// Once registered, the factory automatically creates a v0.3 adapter for agents
/// whose agent card declares protocol version 0.3.
/// </summary>
public static class V03FallbackRegistration
{
    /// <summary>
    /// Registers v0.3 JSON-RPC support with <see cref="A2AClientFactory"/>.
    /// Call this once at application startup.
    /// </summary>
    public static void Register()
    {
        A2AClientFactory.Register(ProtocolBindingNames.JsonRpc, "0.3", CreateV03Client);
    }

    /// <summary>
    /// Creates an <see cref="IA2AClient"/> wrapping a v0.3 JSON-RPC client.
    /// </summary>
    /// <param name="url">The agent's endpoint URL.</param>
    /// <param name="httpClient">Optional HTTP client to use for requests.</param>
    /// <returns>An <see cref="IA2AClient"/> wrapping a v0.3 client.</returns>
    public static IA2AClient CreateV03Client(Uri url, HttpClient? httpClient)
    {
        var v03Client = new V03.A2AClient(url, httpClient);
        return new V03ClientAdapter(v03Client);
    }
}

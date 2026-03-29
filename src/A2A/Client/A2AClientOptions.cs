namespace A2A;

/// <summary>Options for configuring how an <see cref="A2AClientFactory"/> selects a protocol binding.</summary>
public sealed class A2AClientOptions
{
    /// <summary>
    /// Gets or sets the ordered list of preferred protocol bindings.
    /// The first binding that matches a <see cref="AgentInterface"/> in the agent card is selected.
    /// </summary>
    /// <remarks>
    /// Valid values correspond to <see cref="AgentInterface.ProtocolBinding"/>:
    /// <c>"HTTP+JSON"</c> for HTTP+JSON (REST) and <c>"JSONRPC"</c> for JSON-RPC.
    /// The default preference is HTTP+JSON first, with JSON-RPC as fallback.
    /// </remarks>
    public IList<string> PreferredBindings { get; set; } = [ProtocolBindingNames.HttpJson, ProtocolBindingNames.JsonRpc];

    /// <summary>
    /// Gets or sets the path used to fetch the agent card.
    /// Default is <c>/.well-known/agent-card.json</c>.
    /// Only used by <see cref="A2AClientFactory.CreateAsync"/>.
    /// </summary>
    public string AgentCardPath { get; set; } = "/.well-known/agent-card.json";

    /// <summary>
    /// Gets or sets a per-call fallback factory for agent cards that do not declare
    /// <c>supportedInterfaces</c>. When set, this takes priority over the global fallback
    /// registered via <see cref="A2AClientFactory.RegisterFallback"/>.
    /// </summary>
    /// <remarks>
    /// The delegate receives the raw agent card JSON, the base URL, and an optional <see cref="HttpClient"/>,
    /// and should return an <see cref="IA2AClient"/> appropriate for the agent's protocol version.
    /// </remarks>
    public Func<string, Uri, HttpClient?, IA2AClient>? FallbackFactory { get; set; }
}

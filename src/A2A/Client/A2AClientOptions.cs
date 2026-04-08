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
}

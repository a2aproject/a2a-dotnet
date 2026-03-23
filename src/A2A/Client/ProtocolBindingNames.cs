namespace A2A;

/// <summary>Well-known protocol binding names used in <see cref="AgentInterface.ProtocolBinding"/>.</summary>
public static class ProtocolBindingNames
{
    /// <summary>HTTP+JSON (REST) binding.</summary>
    public const string HttpJson = "HTTP+JSON";

    /// <summary>JSON-RPC over HTTP binding.</summary>
    public const string JsonRpc = "JSONRPC";

    /// <summary>gRPC binding.</summary>
    public const string Grpc = "GRPC";
}

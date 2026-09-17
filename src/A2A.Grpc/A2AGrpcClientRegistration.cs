namespace A2A.Grpc;

/// <summary>
/// Registers the gRPC binding with <see cref="A2AClientFactory"/> so that
/// <see cref="A2AClientFactory.Create(AgentCard, HttpClient?, A2AClientOptions?)"/> can resolve a
/// <see cref="A2AGrpcClient"/> for agent interfaces advertising <see cref="ProtocolBindingNames.Grpc"/>.
/// </summary>
public static class A2AGrpcClientRegistration
{
    /// <summary>
    /// Registers the <see cref="ProtocolBindingNames.Grpc"/> binding with <see cref="A2AClientFactory"/>.
    /// Call this once during startup before resolving clients via
    /// <see cref="A2AClientFactory.Create(AgentCard, HttpClient?, A2AClientOptions?)"/>.
    /// </summary>
    public static void Register() =>
        A2AClientFactory.Register(ProtocolBindingNames.Grpc, static (url, httpClient) => new A2AGrpcClient(url, httpClient));
}

namespace A2A.Grpc.AspNetCore;

using global::Grpc.Core;

/// <summary>Configures request-scoped extension negotiation for A2A gRPC services.</summary>
public sealed class A2AGrpcExtensionOptions
{
    /// <summary>Creates gRPC extension hosting options for the authoritative registry.</summary>
    /// <param name="registry">The supported extension and custom operation registry.</param>
    public A2AGrpcExtensionOptions(A2ACustomOperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Registry = registry;
    }

    /// <summary>Gets the authoritative registry.</summary>
    public A2ACustomOperationRegistry Registry { get; }

    /// <summary>Gets an optional availability policy evaluated once per requested supported extension.</summary>
    /// <remarks>
    /// Without a policy, requested supported extensions are activated. Returning false
    /// declines activation. The decision is fixed for the request, including its stream;
    /// it is not an authorization grant or a guarantee of successful execution.
    /// Policy failures and cancellation retain the binding's normal error semantics.
    /// </remarks>
    public Func<ServerCallContext, A2AExtensionRegistration, CancellationToken, ValueTask<bool>>? CanActivate { get; init; }
}

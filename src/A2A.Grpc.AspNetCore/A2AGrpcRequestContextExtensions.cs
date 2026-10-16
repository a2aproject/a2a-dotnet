namespace A2A.Grpc.AspNetCore;

using global::Grpc.Core;

/// <summary>Helpers for passing gRPC request features to typed custom operations.</summary>
public static class A2AGrpcRequestContextExtensions
{
    /// <summary>Creates a custom operation context using this call's negotiated snapshot.</summary>
    /// <param name="context">The current ASP.NET Core gRPC call.</param>
    /// <returns>The host services, HTTP context, gRPC call, and any negotiated activation snapshot.</returns>
    /// <remarks>
    /// Configure extension negotiation for the service before invoking extension-owned
    /// operations. This helper does not negotiate or activate extensions itself; a missing
    /// or foreign snapshot is still rejected by the registry's invocation guard.
    /// </remarks>
    public static A2ACustomOperationContext CreateA2AOperationContext(this ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var httpContext = context.GetHttpContext();
        var activation = httpContext.Features.Get<A2ARequestContext>();
        object[] features = activation is null ? [httpContext, context] : [httpContext, context, activation];
        return new A2ACustomOperationContext(httpContext.RequestServices, features);
    }
}

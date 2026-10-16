using Microsoft.AspNetCore.Http;

namespace A2A.AspNetCore;

/// <summary>
/// Configures request-scoped extension negotiation for HTTP-based A2A endpoints.
/// </summary>
public sealed class A2AExtensionOptions
{
    /// <summary>Creates extension hosting options for the authoritative registry.</summary>
    /// <param name="registry">The registry of supported extensions and custom operations.</param>
    public A2AExtensionOptions(A2ACustomOperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Registry = registry;
    }

    /// <summary>Gets the authoritative extension and custom operation registry.</summary>
    public A2ACustomOperationRegistry Registry { get; }

    /// <summary>
    /// Gets the optional host availability decision, evaluated once per requested supported
    /// extension with the current HTTP context and request cancellation token.
    /// </summary>
    /// <remarks>
    /// Without a policy, requested supported extensions are activated. Returning false
    /// declines activation. Policy failures propagate through the binding's normal error
    /// handling. The result is fixed for the request; it does not grant authorization or
    /// guarantee that dependencies remain available during execution.
    /// </remarks>
    public Func<HttpContext, A2AExtensionRegistration, CancellationToken, ValueTask<bool>>? CanActivate { get; init; }
}

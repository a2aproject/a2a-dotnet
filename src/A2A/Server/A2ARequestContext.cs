using System.Collections.Frozen;

namespace A2A;

/// <summary>
/// Carries immutable extension negotiation results for one A2A request.
/// </summary>
/// <remarks>
/// The registry creates this context after negotiation succeeds. Changes to host
/// availability do not change this snapshot; a later request negotiates separately.
/// This is not an authorization decision or a guarantee of successful execution.
/// </remarks>
public sealed class A2ARequestContext
{
    internal A2ARequestContext(
        object registryToken,
        FrozenSet<string> requestedExtensions,
        FrozenSet<string> activatedExtensions)
    {
        RegistryToken = registryToken;
        RequestedExtensions = requestedExtensions;
        ActivatedExtensions = activatedExtensions;
    }

    /// <summary>Gets the exact extension identifiers requested by the client.</summary>
    public IReadOnlySet<string> RequestedExtensions { get; }

    /// <summary>Gets the extensions activated for this request.</summary>
    public IReadOnlySet<string> ActivatedExtensions { get; }

    internal object RegistryToken { get; }
}

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

    /// <summary>Gets whether the registered extension was activated for this request.</summary>
    /// <param name="registration">An extension registered by the registry that created this context.</param>
    /// <returns><see langword="true"/> when the extension is in the activated snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The registration belongs to a different registry.</exception>
    public bool IsActivated(A2AExtensionRegistration registration)
    {
        EnsureOwnership(registration);
        return ActivatedExtensions.Contains(registration.Uri);
    }

    /// <summary>Gets whether the client requested the registered extension for this request.</summary>
    /// <param name="registration">An extension registered by the registry that created this context.</param>
    /// <returns><see langword="true"/> when the extension is in the requested snapshot, even if activation was declined.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The registration belongs to a different registry.</exception>
    public bool WasRequested(A2AExtensionRegistration registration)
    {
        EnsureOwnership(registration);
        return RequestedExtensions.Contains(registration.Uri);
    }

    internal object RegistryToken { get; }

    private void EnsureOwnership(A2AExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (!ReferenceEquals(registration.RegistryToken, RegistryToken))
        {
            throw new InvalidOperationException("The extension registration does not belong to this registry.");
        }
    }
}

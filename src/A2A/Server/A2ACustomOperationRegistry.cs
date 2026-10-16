using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace A2A;

/// <summary>
/// An immutable registry of typed custom A2A operations and supported extension declarations.
/// </summary>
public sealed class A2ACustomOperationRegistry
{
    private readonly object _registryToken;
    private readonly ReadOnlyDictionary<A2AOperationId, CustomOperationRegistration> _registrations;
    private readonly ReadOnlyDictionary<string, A2AExtensionRegistration> _extensions;
    private readonly A2AExtensionRegistration[] _requiredExtensions;

    internal A2ACustomOperationRegistry(
        object registryToken,
        IReadOnlyDictionary<A2AOperationId, CustomOperationRegistration> registrations,
        IReadOnlyDictionary<string, A2AExtensionRegistration> extensions)
    {
        _registryToken = registryToken;
        _registrations = new ReadOnlyDictionary<A2AOperationId, CustomOperationRegistration>(
            new Dictionary<A2AOperationId, CustomOperationRegistration>(registrations));
        _extensions = new ReadOnlyDictionary<string, A2AExtensionRegistration>(
            new Dictionary<string, A2AExtensionRegistration>(extensions, StringComparer.Ordinal));
        _requiredExtensions = _extensions.Values.Where(extension => extension.Required is true).ToArray();
    }

    /// <summary>
    /// Gets the supported extension registrations, keyed by their exact identifiers.
    /// </summary>
    /// <remarks>These are support declarations, not per-request activation state.</remarks>
    public IReadOnlyDictionary<string, A2AExtensionRegistration> Extensions => _extensions;

    /// <summary>Creates a detached Agent Card with registry-consistent extension declarations.</summary>
    /// <param name="agentCard">The host-provided Agent Card.</param>
    /// <returns>A detached copy; the original card is never modified.</returns>
    /// <remarks>
    /// An unsigned card with no declarations derives them from the registry. Existing
    /// declarations must match in protocol meaning and parameter values. Implicit scalar
    /// defaults are treated equivalently without changing the supplied representation.
    /// Signed cards are never rewritten or stripped of signatures; mismatches require
    /// the host to update and re-sign the card. This does not verify cryptographic signatures.
    /// </remarks>
    public AgentCard PrepareAgentCard(AgentCard agentCard) => A2AExtensionAgentCards.Prepare(this, agentCard);

    /// <summary>Validates the complete extension declarations without changing the card.</summary>
    /// <param name="agentCard">The Agent Card to validate.</param>
    /// <remarks>This validates declaration consistency, not cryptographic signature authenticity.</remarks>
    public void ValidateAgentCard(AgentCard agentCard) => A2AExtensionAgentCards.Validate(this, agentCard);

    /// <summary>
    /// Negotiates supported extensions and freezes the result for one request.
    /// </summary>
    /// <param name="requestedExtensions">Exact, pre-parsed extension identifiers. Unknown identifiers are not activated.</param>
    /// <param name="canActivate">An optional host availability decision, evaluated once per requested supported extension.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>An immutable context that belongs to this registry.</returns>
    /// <remarks>
    /// Identifiers are compared ordinally, without URL normalization or version fallback.
    /// Without a policy, requested supported extensions are activated. A policy returning
    /// false declines activation; required extensions must still be activated. Policy
    /// failures and cancellation propagate without being converted into a declined activation.
    /// Host policies should honor the supplied cancellation token.
    /// The policy is not reevaluated during invocation. Handlers remain responsible for
    /// runtime failures and authorization. No transport headers are parsed or emitted here.
    /// </remarks>
    public async ValueTask<A2ARequestContext> NegotiateExtensionsAsync(
        IEnumerable<string> requestedExtensions,
        Func<A2AExtensionRegistration, CancellationToken, ValueTask<bool>>? canActivate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestedExtensions);
        cancellationToken.ThrowIfCancellationRequested();

        var requested = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in requestedExtensions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier is null)
            {
                throw new ArgumentException("Requested extension identifiers cannot be null.", nameof(requestedExtensions));
            }

            requested.Add(identifier);
        }

        var requestedSnapshot = requested.ToFrozenSet(StringComparer.Ordinal);
        foreach (var extension in _requiredExtensions)
        {
            if (!requestedSnapshot.Contains(extension.Uri))
            {
                throw MissingExtension(extension.Uri);
            }
        }

        var activated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in _extensions.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!requestedSnapshot.Contains(extension.Uri))
            {
                continue;
            }

            var accepted = canActivate is null ||
                await canActivate(extension, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (accepted)
            {
                activated.Add(extension.Uri);
            }
            else if (extension.Required is true)
            {
                throw MissingExtension(extension.Uri);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new A2ARequestContext(
            _registryToken,
            requestedSnapshot,
            activated.ToFrozenSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// Invokes a registered unary custom operation.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="operation">The registered operation handle.</param>
    /// <param name="request">The typed request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The operation result.</returns>
    public ValueTask<TResult> InvokeAsync<TRequest, TResult>(
        A2ACustomOperation<TRequest, TResult> operation,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        return InvokeAsync(operation, new A2ACustomOperationContext(), request, cancellationToken);
    }

    /// <summary>
    /// Invokes a registered server-streaming custom operation.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="operation">The registered operation handle.</param>
    /// <param name="request">The typed request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The operation event stream.</returns>
    public IAsyncEnumerable<TEvent> InvokeStreamingAsync<TRequest, TEvent>(
        A2AStreamingCustomOperation<TRequest, TEvent> operation,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        return InvokeStreamingAsync(operation, new A2ACustomOperationContext(), request, cancellationToken);
    }

    /// <summary>
    /// Invokes a registered unary custom operation with request-scoped host context.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="operation">The registered operation handle.</param>
    /// <param name="context">The request-scoped operation context.</param>
    /// <param name="request">The typed request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The operation result.</returns>
    public ValueTask<TResult> InvokeAsync<TRequest, TResult>(
        A2ACustomOperation<TRequest, TResult> operation,
        A2ACustomOperationContext context,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);
        EnsureOwnership(operation.RegistryToken);
        EnsureActivation(operation.Registration, context);
        return operation.Registration.InvokeAsync(context, request, cancellationToken);
    }

    /// <summary>
    /// Invokes a registered server-streaming custom operation with request-scoped host context.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="operation">The registered operation handle.</param>
    /// <param name="context">The request-scoped operation context.</param>
    /// <param name="request">The typed request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The operation event stream.</returns>
    public IAsyncEnumerable<TEvent> InvokeStreamingAsync<TRequest, TEvent>(
        A2AStreamingCustomOperation<TRequest, TEvent> operation,
        A2ACustomOperationContext context,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);
        EnsureOwnership(operation.RegistryToken);
        EnsureActivation(operation.Registration, context);
        return operation.Registration.InvokeAsync(context, request, cancellationToken);
    }

    internal bool TryGetRegistration(
        A2AOperationId id,
        out CustomOperationRegistration registration) =>
        _registrations.TryGetValue(id, out registration!);

    internal ValueTask<object?> InvokeAsync(
        CustomOperationRegistration registration,
        A2ACustomOperationContext context,
        object request,
        CancellationToken cancellationToken)
    {
        EnsureRegistration(registration);
        EnsureActivation(registration, context);
        return registration.InvokeUnaryAsync(context, request, cancellationToken);
    }

    internal IAsyncEnumerable<object?> InvokeStreamingAsync(
        CustomOperationRegistration registration,
        A2ACustomOperationContext context,
        object request,
        CancellationToken cancellationToken)
    {
        EnsureRegistration(registration);
        EnsureActivation(registration, context);
        return registration.InvokeStreamingAsync(context, request, cancellationToken);
    }

    private void EnsureActivation(CustomOperationRegistration registration, A2ACustomOperationContext context)
    {
        if (!context.TryGetFeature<A2ARequestContext>(out var requestContext))
        {
            if (registration.Extension is { } owner)
            {
                throw MissingExtension(owner.Uri);
            }

            if (_requiredExtensions.Length > 0)
            {
                throw MissingExtension(_requiredExtensions[0].Uri);
            }

            return;
        }

        if (!ReferenceEquals(requestContext.RegistryToken, _registryToken))
        {
            throw new InvalidOperationException("The request context does not belong to this registry.");
        }

        foreach (var extension in _requiredExtensions)
        {
            if (!requestContext.ActivatedExtensions.Contains(extension.Uri))
            {
                throw MissingExtension(extension.Uri);
            }
        }

        if (registration.Extension is { } extensionOwner &&
            !requestContext.ActivatedExtensions.Contains(extensionOwner.Uri))
        {
            throw MissingExtension(extensionOwner.Uri);
        }
    }

    private static A2AException MissingExtension(string identifier) => new(
        $"Extension '{identifier}' must be activated for this request.",
        A2AErrorCode.ExtensionSupportRequired);

    internal void EnsureOwnership(object registryToken)
    {
        if (!ReferenceEquals(_registryToken, registryToken))
        {
            throw new InvalidOperationException("The custom operation handle does not belong to this registry.");
        }
    }

    private void EnsureRegistration(CustomOperationRegistration registration)
    {
        if (!_registrations.TryGetValue(registration.Id, out var registered) ||
            !ReferenceEquals(registered, registration))
        {
            throw new InvalidOperationException("The custom operation registration does not belong to this registry.");
        }
    }
}

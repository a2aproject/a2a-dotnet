using System.Collections.ObjectModel;

namespace A2A;

/// <summary>
/// An immutable registry of typed custom A2A operations.
/// </summary>
public sealed class A2ACustomOperationRegistry
{
    private readonly object _registryToken;
    private readonly ReadOnlyDictionary<A2AOperationId, CustomOperationRegistration> _registrations;

    internal A2ACustomOperationRegistry(
        object registryToken,
        IReadOnlyDictionary<A2AOperationId, CustomOperationRegistration> registrations)
    {
        _registryToken = registryToken;
        _registrations = new ReadOnlyDictionary<A2AOperationId, CustomOperationRegistration>(
            new Dictionary<A2AOperationId, CustomOperationRegistration>(registrations));
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
        ArgumentNullException.ThrowIfNull(operation);
        EnsureOwnership(operation.RegistryToken);
        return operation.Registration.InvokeAsync(request, cancellationToken);
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
        ArgumentNullException.ThrowIfNull(operation);
        EnsureOwnership(operation.RegistryToken);
        return operation.Registration.InvokeAsync(request, cancellationToken);
    }

    internal bool TryGetRegistration(
        A2AOperationId id,
        out CustomOperationRegistration registration) =>
        _registrations.TryGetValue(id, out registration!);

    internal void EnsureOwnership(object registryToken)
    {
        if (!ReferenceEquals(_registryToken, registryToken))
        {
            throw new InvalidOperationException("The custom operation handle does not belong to this registry.");
        }
    }
}

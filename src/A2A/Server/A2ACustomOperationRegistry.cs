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
        return operation.Registration.InvokeAsync(
            new A2ACustomOperationContext(),
            request,
            cancellationToken);
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
        return operation.Registration.InvokeAsync(
            new A2ACustomOperationContext(),
            request,
            cancellationToken);
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
        return registration.InvokeUnaryAsync(context, request, cancellationToken);
    }

    internal IAsyncEnumerable<object?> InvokeStreamingAsync(
        CustomOperationRegistration registration,
        A2ACustomOperationContext context,
        object request,
        CancellationToken cancellationToken)
    {
        EnsureRegistration(registration);
        return registration.InvokeStreamingAsync(context, request, cancellationToken);
    }

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

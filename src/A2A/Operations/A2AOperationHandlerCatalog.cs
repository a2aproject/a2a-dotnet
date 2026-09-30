namespace A2A;

/// <summary>Handles a typed unary A2A operation.</summary>
/// <typeparam name="TRequest">The operation request type.</typeparam>
/// <typeparam name="TResult">The operation result type.</typeparam>
/// <param name="context">The operation context.</param>
/// <param name="request">The operation request.</param>
/// <param name="cancellationToken">A cancellation token.</param>
/// <returns>The operation result.</returns>
public delegate ValueTask<TResult> A2AOperationHandler<TRequest, TResult>(
    A2AOperationContext context,
    TRequest request,
    CancellationToken cancellationToken);

internal sealed record A2AOperationHandlerRegistration<TRequest, TResult>(
    A2AOperationHandler<TRequest, TResult> Handler);

/// <summary>Builds an immutable catalog of typed unary operation handlers.</summary>
public sealed class A2AOperationHandlerCatalogBuilder
{
    private readonly Dictionary<A2AOperationId, object> _registrations = [];

    /// <summary>Registers a handler for an operation.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="operation">The operation definition.</param>
    /// <param name="handler">The operation handler.</param>
    /// <returns>This builder.</returns>
    public A2AOperationHandlerCatalogBuilder Map<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation,
        A2AOperationHandler<TRequest, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(handler);

        if (!_registrations.TryAdd(
            operation.Id,
            new A2AOperationHandlerRegistration<TRequest, TResult>(handler)))
        {
            throw new InvalidOperationException(
                $"An A2A operation handler is already registered for '{operation.Id.Value}'.");
        }

        return this;
    }

    /// <summary>Builds the immutable handler catalog.</summary>
    /// <returns>The handler catalog.</returns>
    public A2AOperationHandlerCatalog Build()
        => new(_registrations);
}

/// <summary>Invokes typed unary operation handlers.</summary>
public sealed class A2AOperationHandlerCatalog
{
    private readonly Dictionary<A2AOperationId, object> _registrations;

    internal A2AOperationHandlerCatalog(
        IReadOnlyDictionary<A2AOperationId, object> registrations)
    {
        _registrations = new Dictionary<A2AOperationId, object>(registrations);
    }

    /// <summary>Invokes the handler registered for an operation.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="operation">The operation definition.</param>
    /// <param name="context">The operation context.</param>
    /// <param name="request">The operation request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The operation result.</returns>
    public ValueTask<TResult> InvokeAsync<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation,
        A2AOperationContext context,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        if (!_registrations.TryGetValue(operation.Id, out var registration))
        {
            throw new InvalidOperationException(
                $"No A2A operation handler is registered for '{operation.Id.Value}'.");
        }

        if (registration is not A2AOperationHandlerRegistration<TRequest, TResult> typedRegistration)
        {
            throw new InvalidOperationException(
                $"The A2A operation handler for '{operation.Id.Value}' has incompatible request or result types.");
        }

        return typedRegistration.Handler(context, request, cancellationToken);
    }
}

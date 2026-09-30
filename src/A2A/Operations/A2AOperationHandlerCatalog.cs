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

/// <summary>Handles a typed streaming A2A operation.</summary>
/// <typeparam name="TRequest">The operation request type.</typeparam>
/// <typeparam name="TEvent">The streamed event type.</typeparam>
/// <param name="context">The operation context.</param>
/// <param name="request">The operation request.</param>
/// <param name="cancellationToken">A cancellation token.</param>
/// <returns>The streamed operation events.</returns>
public delegate IAsyncEnumerable<TEvent> A2AStreamingOperationHandler<TRequest, TEvent>(
    A2AOperationContext context,
    TRequest request,
    CancellationToken cancellationToken);

internal interface IA2AOperationHandlerRegistration
{
    A2AOperationId Id { get; }

    A2AOperationKind Kind { get; }

    Type RequestType { get; }

    Type ResponseType { get; }

    object Operation { get; }
}

internal sealed record A2AOperationHandlerRegistration<TRequest, TResult>(
    A2AOperation<TRequest, TResult> OperationHandle,
    A2AOperationHandler<TRequest, TResult> Handler)
    : IA2AOperationHandlerRegistration
{
    public A2AOperationId Id => OperationHandle.Id;

    public A2AOperationKind Kind => A2AOperationKind.Unary;

    public Type RequestType => typeof(TRequest);

    public Type ResponseType => typeof(TResult);

    public object Operation => OperationHandle;
}

internal sealed record A2AStreamingOperationHandlerRegistration<TRequest, TEvent>(
    A2AStreamingOperation<TRequest, TEvent> OperationHandle,
    A2AStreamingOperationHandler<TRequest, TEvent> Handler)
    : IA2AOperationHandlerRegistration
{
    public A2AOperationId Id => OperationHandle.Id;

    public A2AOperationKind Kind => A2AOperationKind.Streaming;

    public Type RequestType => typeof(TRequest);

    public Type ResponseType => typeof(TEvent);

    public object Operation => OperationHandle;
}

/// <summary>Builds an immutable catalog of typed unary and streaming operation handlers.</summary>
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
            new A2AOperationHandlerRegistration<TRequest, TResult>(operation, handler)))
        {
            throw new InvalidOperationException(
                $"An A2A operation handler is already registered for '{operation.Id.Value}'.");
        }

        return this;
    }

    /// <summary>Registers a handler for a streaming operation.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="operation">The streaming operation definition.</param>
    /// <param name="handler">The streaming operation handler.</param>
    /// <returns>This builder.</returns>
    public A2AOperationHandlerCatalogBuilder MapStreaming<TRequest, TEvent>(
        A2AStreamingOperation<TRequest, TEvent> operation,
        A2AStreamingOperationHandler<TRequest, TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(handler);

        if (!_registrations.TryAdd(
            operation.Id,
            new A2AStreamingOperationHandlerRegistration<TRequest, TEvent>(
                operation,
                handler)))
        {
            throw new InvalidOperationException(
                $"An A2A operation handler is already registered for '{operation.Id.Value}'.");
        }

        return this;
    }

    /// <summary>Builds the immutable handler catalog.</summary>
    /// <param name="operationCatalog">The operation-definition catalog.</param>
    /// <returns>The handler catalog.</returns>
    public A2AOperationHandlerCatalog Build(A2AOperationCatalog operationCatalog)
    {
        ArgumentNullException.ThrowIfNull(operationCatalog);

        foreach (var registration in _registrations.Values.Cast<IA2AOperationHandlerRegistration>())
        {
            ValidateRegistration(operationCatalog, registration);
        }

        return new(operationCatalog, _registrations);
    }

    private static void ValidateRegistration(
        A2AOperationCatalog operationCatalog,
        IA2AOperationHandlerRegistration registration)
    {
        if (!operationCatalog.TryGetRegistration(registration.Id, out var operationRegistration))
        {
            throw new InvalidOperationException(
                $"The A2A operation '{registration.Id.Value}' is not defined by the supplied catalog.");
        }

        if (operationRegistration.Kind != registration.Kind)
        {
            throw new InvalidOperationException(
                $"The A2A operation '{registration.Id.Value}' is registered as {operationRegistration.Kind} instead of {registration.Kind}.");
        }

        if (operationRegistration.RequestType != registration.RequestType
            || operationRegistration.ResponseType != registration.ResponseType)
        {
            throw new InvalidOperationException(
                $"The {registration.Kind} A2A operation '{registration.Id.Value}' uses incompatible request or response types '{registration.RequestType.FullName}' and '{registration.ResponseType.FullName}'.");
        }

        if (!ReferenceEquals(operationRegistration.Handle, registration.Operation))
        {
            throw new InvalidOperationException(
                $"The A2A operation '{registration.Id.Value}' does not belong to the supplied catalog.");
        }
    }
}

/// <summary>Invokes typed unary and streaming operation handlers.</summary>
public sealed class A2AOperationHandlerCatalog
{
    private readonly A2AOperationCatalog _operationCatalog;
    private readonly Dictionary<A2AOperationId, object> _registrations;

    internal A2AOperationHandlerCatalog(
        A2AOperationCatalog operationCatalog,
        IReadOnlyDictionary<A2AOperationId, object> registrations)
    {
        _operationCatalog = operationCatalog;
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

        if (!ReferenceEquals(typedRegistration.OperationHandle, operation))
        {
            throw new InvalidOperationException(
                $"The A2A operation '{operation.Id.Value}' does not belong to this handler catalog.");
        }

        _operationCatalog.Validate(operation, request);
        return typedRegistration.Handler(context, request, cancellationToken);
    }

    /// <summary>Invokes the handler registered for a streaming operation.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="operation">The streaming operation definition.</param>
    /// <param name="context">The operation context.</param>
    /// <param name="request">The operation request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The streamed events.</returns>
    public IAsyncEnumerable<TEvent> InvokeStreamingAsync<TRequest, TEvent>(
        A2AStreamingOperation<TRequest, TEvent> operation,
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

        if (registration is not A2AStreamingOperationHandlerRegistration<TRequest, TEvent> typedRegistration)
        {
            throw new InvalidOperationException(
                $"The A2A operation handler for '{operation.Id.Value}' has incompatible request or result types.");
        }

        if (!ReferenceEquals(typedRegistration.OperationHandle, operation))
        {
            throw new InvalidOperationException(
                $"The A2A operation '{operation.Id.Value}' does not belong to this handler catalog.");
        }

        _operationCatalog.ValidateStreaming(operation, request);
        return typedRegistration.Handler(context, request, cancellationToken);
    }
}

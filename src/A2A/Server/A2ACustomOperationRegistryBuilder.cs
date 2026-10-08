using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>
/// Builds an immutable registry of typed custom A2A operations.
/// </summary>
public sealed class A2ACustomOperationRegistryBuilder
{
    private readonly object _registryToken = new();
    private readonly Dictionary<A2AOperationId, CustomOperationRegistration> _registrations = [];
    private bool _built;

    /// <summary>
    /// Registers a unary custom operation.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="id">The stable operation identifier.</param>
    /// <param name="handler">The typed operation handler.</param>
    /// <param name="requestTypeInfo">Source-generated request JSON metadata.</param>
    /// <param name="resultTypeInfo">Source-generated result JSON metadata.</param>
    /// <param name="validator">An optional semantic request validator.</param>
    /// <returns>A typed handle for the registered operation.</returns>
    public A2ACustomOperation<TRequest, TResult> Map<TRequest, TResult>(
        A2AOperationId id,
        A2ACustomOperationHandler<TRequest, TResult> handler,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        A2ACustomOperationValidator<TRequest>? validator = null)
    {
        EnsureCanRegister(id);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(resultTypeInfo);
        EnsureClosedTypes<TRequest, TResult>();

        return Map(
            id,
            (_, request, cancellationToken) => handler(request, cancellationToken),
            requestTypeInfo,
            resultTypeInfo,
            validator);
    }

    /// <summary>
    /// Registers a unary custom operation with request-scoped host context.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="id">The stable operation identifier.</param>
    /// <param name="handler">The contextual operation handler.</param>
    /// <param name="requestTypeInfo">Source-generated request JSON metadata.</param>
    /// <param name="resultTypeInfo">Source-generated result JSON metadata.</param>
    /// <param name="validator">An optional semantic request validator.</param>
    /// <returns>A typed handle for the registered operation.</returns>
    public A2ACustomOperation<TRequest, TResult> Map<TRequest, TResult>(
        A2AOperationId id,
        A2AContextualCustomOperationHandler<TRequest, TResult> handler,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        A2ACustomOperationValidator<TRequest>? validator = null)
    {
        EnsureCanRegister(id);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(resultTypeInfo);
        EnsureClosedTypes<TRequest, TResult>();

        var registration = new UnaryCustomOperationRegistration<TRequest, TResult>(
            id,
            handler,
            requestTypeInfo,
            resultTypeInfo,
            validator);
        _registrations.Add(id, registration);
        return new A2ACustomOperation<TRequest, TResult>(id, _registryToken, registration);
    }

    /// <summary>
    /// Registers a server-streaming custom operation.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="id">The stable operation identifier.</param>
    /// <param name="handler">The typed streaming handler.</param>
    /// <param name="requestTypeInfo">Source-generated request JSON metadata.</param>
    /// <param name="eventTypeInfo">Source-generated event JSON metadata.</param>
    /// <param name="validator">An optional semantic request validator.</param>
    /// <returns>A typed handle for the registered operation.</returns>
    public A2AStreamingCustomOperation<TRequest, TEvent> MapStreaming<TRequest, TEvent>(
        A2AOperationId id,
        A2AStreamingCustomOperationHandler<TRequest, TEvent> handler,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TEvent> eventTypeInfo,
        A2ACustomOperationValidator<TRequest>? validator = null)
    {
        EnsureCanRegister(id);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(eventTypeInfo);
        EnsureClosedTypes<TRequest, TEvent>();

        return MapStreaming(
            id,
            (_, request, cancellationToken) => handler(request, cancellationToken),
            requestTypeInfo,
            eventTypeInfo,
            validator);
    }

    /// <summary>
    /// Registers a server-streaming custom operation with request-scoped host context.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="id">The stable operation identifier.</param>
    /// <param name="handler">The contextual streaming handler.</param>
    /// <param name="requestTypeInfo">Source-generated request JSON metadata.</param>
    /// <param name="eventTypeInfo">Source-generated event JSON metadata.</param>
    /// <param name="validator">An optional semantic request validator.</param>
    /// <returns>A typed handle for the registered operation.</returns>
    public A2AStreamingCustomOperation<TRequest, TEvent> MapStreaming<TRequest, TEvent>(
        A2AOperationId id,
        A2AContextualStreamingCustomOperationHandler<TRequest, TEvent> handler,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TEvent> eventTypeInfo,
        A2ACustomOperationValidator<TRequest>? validator = null)
    {
        EnsureCanRegister(id);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(eventTypeInfo);
        EnsureClosedTypes<TRequest, TEvent>();

        var registration = new StreamingCustomOperationRegistration<TRequest, TEvent>(
            id,
            handler,
            requestTypeInfo,
            eventTypeInfo,
            validator);
        _registrations.Add(id, registration);
        return new A2AStreamingCustomOperation<TRequest, TEvent>(id, _registryToken, registration);
    }

    /// <summary>
    /// Finalizes the registry.
    /// </summary>
    /// <returns>The immutable custom operation registry.</returns>
    public A2ACustomOperationRegistry Build()
    {
        if (_built)
        {
            throw new InvalidOperationException("The custom operation registry has already been built.");
        }

        _built = true;
        return new A2ACustomOperationRegistry(_registryToken, _registrations);
    }

    private void EnsureCanRegister(A2AOperationId id)
    {
        if (_built)
        {
            throw new InvalidOperationException("The custom operation registry has already been built.");
        }

        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("The operation ID cannot be empty.", nameof(id));
        }

        if (_registrations.ContainsKey(id))
        {
            throw new InvalidOperationException($"A custom operation with ID '{id}' is already registered.");
        }
    }

    private static void EnsureClosedTypes<TRequest, TOutput>()
    {
        if (typeof(TRequest).ContainsGenericParameters || typeof(TOutput).ContainsGenericParameters)
        {
            throw new ArgumentException("Custom operations require closed request and output types.");
        }
    }
}

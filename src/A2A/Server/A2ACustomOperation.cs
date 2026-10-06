using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>
/// Handles a unary custom A2A operation.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
/// <param name="request">The typed request.</param>
/// <param name="cancellationToken">The request cancellation token.</param>
public delegate ValueTask<TResult> A2ACustomOperationHandler<TRequest, TResult>(
    TRequest request,
    CancellationToken cancellationToken);

/// <summary>
/// Handles a server-streaming custom A2A operation.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TEvent">The streamed event type.</typeparam>
/// <param name="request">The typed request.</param>
/// <param name="cancellationToken">The request cancellation token.</param>
public delegate IAsyncEnumerable<TEvent> A2AStreamingCustomOperationHandler<TRequest, TEvent>(
    TRequest request,
    CancellationToken cancellationToken);

/// <summary>
/// Validates a custom A2A operation request before handler invocation.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <param name="request">The typed request.</param>
public delegate void A2ACustomOperationValidator<TRequest>(TRequest request);

/// <summary>
/// A typed handle for a registered unary custom A2A operation.
/// </summary>
public sealed class A2ACustomOperation<TRequest, TResult>
{
    internal A2ACustomOperation(
        A2AOperationId id,
        object registryToken,
        UnaryCustomOperationRegistration<TRequest, TResult> registration)
    {
        Id = id;
        RegistryToken = registryToken;
        Registration = registration;
    }

    /// <summary>
    /// Gets the stable operation identifier.
    /// </summary>
    public A2AOperationId Id { get; }

    internal object RegistryToken { get; }

    internal UnaryCustomOperationRegistration<TRequest, TResult> Registration { get; }
}

/// <summary>
/// A typed handle for a registered server-streaming custom A2A operation.
/// </summary>
public sealed class A2AStreamingCustomOperation<TRequest, TEvent>
{
    internal A2AStreamingCustomOperation(
        A2AOperationId id,
        object registryToken,
        StreamingCustomOperationRegistration<TRequest, TEvent> registration)
    {
        Id = id;
        RegistryToken = registryToken;
        Registration = registration;
    }

    /// <summary>
    /// Gets the stable operation identifier.
    /// </summary>
    public A2AOperationId Id { get; }

    internal object RegistryToken { get; }

    internal StreamingCustomOperationRegistration<TRequest, TEvent> Registration { get; }
}

internal enum A2ACustomOperationKind
{
    Unary,
    Streaming,
}

internal abstract class CustomOperationRegistration
{
    protected CustomOperationRegistration(
        A2AOperationId id,
        JsonTypeInfo requestTypeInfo,
        JsonTypeInfo outputTypeInfo,
        A2ACustomOperationKind kind)
    {
        Id = id;
        RequestTypeInfo = requestTypeInfo;
        OutputTypeInfo = outputTypeInfo;
        Kind = kind;
    }

    internal A2AOperationId Id { get; }

    internal JsonTypeInfo RequestTypeInfo { get; }

    internal JsonTypeInfo OutputTypeInfo { get; }

    internal A2ACustomOperationKind Kind { get; }

    internal virtual ValueTask<object?> InvokeUnaryAsync(object request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"Operation '{Id}' is not unary.");

    internal virtual IAsyncEnumerable<object?> InvokeStreamingAsync(
        object request,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"Operation '{Id}' is not streaming.");
}

internal sealed class UnaryCustomOperationRegistration<TRequest, TResult> : CustomOperationRegistration
{
    private readonly A2ACustomOperationHandler<TRequest, TResult> _handler;
    private readonly A2ACustomOperationValidator<TRequest>? _validator;

    internal UnaryCustomOperationRegistration(
        A2AOperationId id,
        A2ACustomOperationHandler<TRequest, TResult> handler,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        A2ACustomOperationValidator<TRequest>? validator)
        : base(id, requestTypeInfo, resultTypeInfo, A2ACustomOperationKind.Unary)
    {
        _handler = handler;
        _validator = validator;
    }

    internal async ValueTask<TResult> InvokeAsync(TRequest request, CancellationToken cancellationToken)
    {
        using var activity = A2ACustomOperationDiagnostics.Start(Id, A2ACustomOperationKind.Unary);

        try
        {
            _validator?.Invoke(request);
            var result = await _handler(request, cancellationToken).ConfigureAwait(false);
            A2ACustomOperationDiagnostics.SetSuccess(activity);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            A2ACustomOperationDiagnostics.SetCancelled(activity);
            throw;
        }
        catch (Exception exception)
        {
            A2ACustomOperationDiagnostics.SetError(activity, exception);
            throw;
        }
    }

    internal override async ValueTask<object?> InvokeUnaryAsync(object request, CancellationToken cancellationToken) =>
        await InvokeAsync((TRequest)request, cancellationToken).ConfigureAwait(false);
}

internal sealed class StreamingCustomOperationRegistration<TRequest, TEvent> : CustomOperationRegistration
{
    private readonly A2AStreamingCustomOperationHandler<TRequest, TEvent> _handler;
    private readonly A2ACustomOperationValidator<TRequest>? _validator;

    internal StreamingCustomOperationRegistration(
        A2AOperationId id,
        A2AStreamingCustomOperationHandler<TRequest, TEvent> handler,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TEvent> eventTypeInfo,
        A2ACustomOperationValidator<TRequest>? validator)
        : base(id, requestTypeInfo, eventTypeInfo, A2ACustomOperationKind.Streaming)
    {
        _handler = handler;
        _validator = validator;
    }

    internal async IAsyncEnumerable<TEvent> InvokeAsync(
        TRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = A2ACustomOperationDiagnostics.Start(Id, A2ACustomOperationKind.Streaming);
        IAsyncEnumerator<TEvent> enumerator;

        try
        {
            _validator?.Invoke(request);
            enumerator = _handler(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            A2ACustomOperationDiagnostics.SetCancelled(activity);
            throw;
        }
        catch (Exception exception)
        {
            A2ACustomOperationDiagnostics.SetError(activity, exception);
            throw;
        }

        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                TEvent streamEvent;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        A2ACustomOperationDiagnostics.SetSuccess(activity);
                        yield break;
                    }

                    streamEvent = enumerator.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    A2ACustomOperationDiagnostics.SetCancelled(activity);
                    throw;
                }
                catch (Exception exception)
                {
                    A2ACustomOperationDiagnostics.SetError(activity, exception);
                    throw;
                }

                yield return streamEvent;
            }
        }
    }

    internal override async IAsyncEnumerable<object?> InvokeStreamingAsync(
        object request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var streamEvent in InvokeAsync((TRequest)request, cancellationToken).ConfigureAwait(false))
        {
            yield return streamEvent;
        }
    }
}

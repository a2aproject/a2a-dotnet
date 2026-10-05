namespace A2A.Grpc.AspNetCore;

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

/// <summary>
/// Builds gRPC envelope bindings for custom/extension A2A operations — those registered via
/// <see cref="A2AOperationCatalogBuilder.DefineUnary{TRequest,TResult}"/>/<c>DefineStreaming</c> that have
/// no fixed RPC in the vendored <c>a2a.proto</c> contract.
/// </summary>
/// <remarks>
/// Unlike the JSON-RPC/HTTP+JSON bindings, which map an operation to a method name or route, the gRPC
/// envelope addresses an operation directly by its transport-neutral <see cref="A2AOperationId"/>, so only
/// the operation and its <see cref="JsonTypeInfo{T}"/> serialization metadata need to be supplied.
/// </remarks>
public sealed class A2AGrpcExtensionOperationBindingBuilder
{
    private readonly Dictionary<string, IA2AGrpcExtensionOperationBinding> _bindings =
        new(StringComparer.Ordinal);

    /// <summary>Maps a typed unary operation for gRPC envelope dispatch.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="operation">The operation definition.</param>
    /// <param name="requestTypeInfo">Request serialization metadata.</param>
    /// <param name="resultTypeInfo">Result serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AGrpcExtensionOperationBindingBuilder Map<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        AddBinding(
            operation.Id.Value,
            new A2AGrpcExtensionUnaryOperationBinding<TRequest, TResult>(
                operation,
                requestTypeInfo,
                resultTypeInfo));
        return this;
    }

    /// <summary>Maps a typed streaming operation for gRPC envelope dispatch.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="operation">The streaming operation definition.</param>
    /// <param name="requestTypeInfo">Request serialization metadata.</param>
    /// <param name="eventTypeInfo">Event serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AGrpcExtensionOperationBindingBuilder MapStreaming<TRequest, TEvent>(
        A2AStreamingOperation<TRequest, TEvent> operation,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TEvent> eventTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(eventTypeInfo);

        AddBinding(
            operation.Id.Value,
            new A2AGrpcExtensionStreamingOperationBinding<TRequest, TEvent>(
                operation,
                requestTypeInfo,
                eventTypeInfo));
        return this;
    }

    /// <summary>Builds the immutable gRPC extension binding table.</summary>
    /// <param name="operationCatalog">The operation-definition catalog.</param>
    /// <returns>The binding table.</returns>
    public A2AGrpcExtensionOperationBindings Build(A2AOperationCatalog operationCatalog)
    {
        ArgumentNullException.ThrowIfNull(operationCatalog);

        foreach (var binding in _bindings.Values)
        {
            binding.Validate(operationCatalog);
        }

        return new A2AGrpcExtensionOperationBindings(_bindings);
    }

    private void AddBinding(string operationId, IA2AGrpcExtensionOperationBinding binding)
    {
        if (!_bindings.TryAdd(operationId, binding))
        {
            throw new InvalidOperationException(
                $"A gRPC extension operation binding is already registered for operation '{operationId}'.");
        }
    }
}

/// <summary>Resolves A2A operation ids to gRPC-envelope-reachable extension operations.</summary>
public sealed class A2AGrpcExtensionOperationBindings
{
    private readonly Dictionary<string, IA2AGrpcExtensionOperationBinding> _bindings;

    internal A2AGrpcExtensionOperationBindings(
        IReadOnlyDictionary<string, IA2AGrpcExtensionOperationBinding> bindings)
    {
        _bindings = new Dictionary<string, IA2AGrpcExtensionOperationBinding>(
            bindings,
            StringComparer.Ordinal);
    }

    internal bool TryGetBinding(string operationId, out IA2AGrpcExtensionOperationBinding binding)
        => _bindings.TryGetValue(operationId, out binding!);
}

internal interface IA2AGrpcExtensionOperationBinding
{
    A2AOperationKind Kind { get; }

    void Validate(A2AOperationCatalog operationCatalog);
}

internal interface IA2AGrpcExtensionUnaryOperationBinding : IA2AGrpcExtensionOperationBinding
{
    ValueTask<byte[]> InvokeAsync(
        A2AOperationContext context,
        A2AOperationHandlerCatalog handlers,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);
}

internal interface IA2AGrpcExtensionStreamingOperationBinding : IA2AGrpcExtensionOperationBinding
{
    IAsyncEnumerable<byte[]> InvokeAsync(
        A2AOperationContext context,
        A2AOperationHandlerCatalog handlers,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);
}

// Shared JSON (de)serialization helpers for the envelope payload, so request/response/event handling is
// consistent across the unary and streaming binding implementations.
internal static class A2AGrpcExtensionOperationBinding
{
    public static TRequest Deserialize<TRequest>(
        ReadOnlyMemory<byte> payload,
        JsonTypeInfo<TRequest> requestTypeInfo,
        string operationId)
    {
        try
        {
            var request = JsonSerializer.Deserialize(payload.Span, requestTypeInfo);
            if (request is null)
            {
                throw new A2AException(
                    $"Failed to deserialize the extension operation payload for '{operationId}' as {typeof(TRequest).Name}.",
                    A2AErrorCode.InvalidParams);
            }

            return request;
        }
        catch (JsonException ex)
        {
            throw new A2AException(
                $"Invalid extension operation payload for '{operationId}': could not be deserialized as {typeof(TRequest).Name}.",
                ex,
                A2AErrorCode.InvalidParams);
        }
    }

    public static void ValidateRegistration(
        A2AOperationCatalog operationCatalog,
        object operation,
        A2AOperationId operationId,
        A2AOperationKind kind,
        Type requestType,
        Type responseType)
    {
        if (!operationCatalog.TryGetRegistration(operationId, out var registration)
            || !ReferenceEquals(registration.Handle, operation))
        {
            throw new InvalidOperationException(
                $"The gRPC extension binding operation '{operationId.Value}' is not defined by the supplied operation catalog.");
        }

        if (registration.Kind != kind
            || registration.RequestType != requestType
            || registration.ResponseType != responseType)
        {
            throw new InvalidOperationException(
                $"The gRPC extension binding for '{operationId.Value}' uses incompatible operation types.");
        }
    }
}

internal sealed class A2AGrpcExtensionUnaryOperationBinding<TRequest, TResult>(
    A2AOperation<TRequest, TResult> operation,
    JsonTypeInfo<TRequest> requestTypeInfo,
    JsonTypeInfo<TResult> resultTypeInfo)
    : IA2AGrpcExtensionUnaryOperationBinding
{
    public A2AOperationKind Kind => A2AOperationKind.Unary;

    public void Validate(A2AOperationCatalog operationCatalog)
        => A2AGrpcExtensionOperationBinding.ValidateRegistration(
            operationCatalog,
            operation,
            operation.Id,
            A2AOperationKind.Unary,
            typeof(TRequest),
            typeof(TResult));

    public async ValueTask<byte[]> InvokeAsync(
        A2AOperationContext context,
        A2AOperationHandlerCatalog handlers,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var request = A2AGrpcExtensionOperationBinding.Deserialize(
            payload,
            requestTypeInfo,
            operation.Id.Value);
        handlers.OperationCatalog.Validate(operation, request);
        var result = await handlers.InvokeAsync(operation, context, request, cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.SerializeToUtf8Bytes(result, resultTypeInfo);
    }
}

internal sealed class A2AGrpcExtensionStreamingOperationBinding<TRequest, TEvent>(
    A2AStreamingOperation<TRequest, TEvent> operation,
    JsonTypeInfo<TRequest> requestTypeInfo,
    JsonTypeInfo<TEvent> eventTypeInfo)
    : IA2AGrpcExtensionStreamingOperationBinding
{
    public A2AOperationKind Kind => A2AOperationKind.Streaming;

    public void Validate(A2AOperationCatalog operationCatalog)
        => A2AGrpcExtensionOperationBinding.ValidateRegistration(
            operationCatalog,
            operation,
            operation.Id,
            A2AOperationKind.Streaming,
            typeof(TRequest),
            typeof(TEvent));

    public IAsyncEnumerable<byte[]> InvokeAsync(
        A2AOperationContext context,
        A2AOperationHandlerCatalog handlers,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var request = A2AGrpcExtensionOperationBinding.Deserialize(
            payload,
            requestTypeInfo,
            operation.Id.Value);
        handlers.OperationCatalog.ValidateStreaming(operation, request);
        return InvokeStreamingCoreAsync(context, handlers, request, cancellationToken);
    }

    private async IAsyncEnumerable<byte[]> InvokeStreamingCoreAsync(
        A2AOperationContext context,
        A2AOperationHandlerCatalog handlers,
        TRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var streamEvent in handlers.InvokeStreamingAsync(
            operation,
            context,
            request,
            cancellationToken).ConfigureAwait(false))
        {
            yield return JsonSerializer.SerializeToUtf8Bytes(streamEvent, eventTypeInfo);
        }
    }
}

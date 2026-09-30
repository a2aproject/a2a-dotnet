using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A.AspNetCore;

/// <summary>Builds JSON-RPC bindings for typed custom unary operations.</summary>
public sealed class A2AJsonRpcOperationBindingBuilder
{
    private readonly Dictionary<string, IA2AJsonRpcOperationBinding> _bindings =
        new(StringComparer.Ordinal);

    /// <summary>Maps a JSON-RPC method to a typed unary operation.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="method">The JSON-RPC method name.</param>
    /// <param name="operation">The operation definition.</param>
    /// <param name="requestTypeInfo">Request serialization metadata.</param>
    /// <param name="resultTypeInfo">Result serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AJsonRpcOperationBindingBuilder Map<TRequest, TResult>(
        string method,
        A2AOperation<TRequest, TResult> operation,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        if (!_bindings.TryAdd(
            method,
            new A2AJsonRpcOperationBinding<TRequest, TResult>(
                operation,
                requestTypeInfo,
                resultTypeInfo)))
        {
            throw new InvalidOperationException(
                $"A JSON-RPC operation binding is already registered for method '{method}'.");
        }

        return this;
    }

    /// <summary>Builds the immutable JSON-RPC binding table.</summary>
    /// <returns>The binding table.</returns>
    public A2AJsonRpcOperationBindings Build()
        => new(_bindings);
}

/// <summary>Resolves JSON-RPC methods to typed custom unary operations.</summary>
public sealed class A2AJsonRpcOperationBindings
{
    private readonly Dictionary<string, IA2AJsonRpcOperationBinding> _bindings;

    internal A2AJsonRpcOperationBindings(
        IReadOnlyDictionary<string, IA2AJsonRpcOperationBinding> bindings)
    {
        _bindings = new Dictionary<string, IA2AJsonRpcOperationBinding>(
            bindings,
            StringComparer.Ordinal);
    }

    internal bool TryGetBinding(
        string method,
        out IA2AJsonRpcOperationBinding binding)
        => _bindings.TryGetValue(method, out binding!);
}

internal interface IA2AJsonRpcOperationBinding
{
    ValueTask<JsonRpcResponse> InvokeAsync(
        JsonRpcId requestId,
        JsonElement parameters,
        A2AOperationContext context,
        A2AOperationHandlerCatalog handlers,
        CancellationToken cancellationToken);
}

internal sealed class A2AJsonRpcOperationBinding<TRequest, TResult>(
    A2AOperation<TRequest, TResult> operation,
    JsonTypeInfo<TRequest> requestTypeInfo,
    JsonTypeInfo<TResult> resultTypeInfo)
    : IA2AJsonRpcOperationBinding
{
    public async ValueTask<JsonRpcResponse> InvokeAsync(
        JsonRpcId requestId,
        JsonElement parameters,
        A2AOperationContext context,
        A2AOperationHandlerCatalog handlers,
        CancellationToken cancellationToken)
    {
        TRequest request;
        try
        {
            request = parameters.Deserialize(requestTypeInfo)
                ?? throw new A2AException(
                    $"Failed to deserialize parameters as {typeof(TRequest).Name}.",
                    A2AErrorCode.InvalidParams);
        }
        catch (JsonException ex)
        {
            throw new A2AException(
                $"Invalid parameters: request body could not be deserialized as {typeof(TRequest).Name}.",
                ex,
                A2AErrorCode.InvalidParams);
        }

        var result = await handlers.InvokeAsync(
            operation,
            context,
            request,
            cancellationToken).ConfigureAwait(false);
        return JsonRpcResponse.CreateJsonRpcResponse(
            requestId,
            result,
            resultTypeInfo);
    }
}

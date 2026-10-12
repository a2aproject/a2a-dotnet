namespace A2A.AspNetCore;

/// <summary>
/// Builds JSON-RPC method mappings for custom A2A operations.
/// </summary>
public sealed class A2AJsonRpcCustomOperationBuilder
{
    private readonly Dictionary<string, JsonRpcCustomOperationBinding> _bindings =
        new(StringComparer.Ordinal);
    private bool _built;

    /// <summary>
    /// Maps a JSON-RPC method to a unary custom operation.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="method">The JSON-RPC method name.</param>
    /// <param name="operation">The custom operation handle.</param>
    /// <returns>This builder.</returns>
    public A2AJsonRpcCustomOperationBuilder Map<TRequest, TResult>(
        string method,
        A2ACustomOperation<TRequest, TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Add(method, operation.RegistryToken, operation.Registration);
        return this;
    }

    /// <summary>
    /// Maps a JSON-RPC method to a streaming custom operation.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="method">The JSON-RPC method name.</param>
    /// <param name="operation">The custom operation handle.</param>
    /// <returns>This builder.</returns>
    public A2AJsonRpcCustomOperationBuilder MapStreaming<TRequest, TEvent>(
        string method,
        A2AStreamingCustomOperation<TRequest, TEvent> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Add(method, operation.RegistryToken, operation.Registration);
        return this;
    }

    /// <summary>
    /// Finalizes the JSON-RPC custom operation mappings.
    /// </summary>
    /// <param name="registry">The registry that owns every mapped operation.</param>
    /// <returns>The immutable JSON-RPC custom operation bindings.</returns>
    public A2AJsonRpcCustomOperationBindings Build(A2ACustomOperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (_built)
        {
            throw new InvalidOperationException("The JSON-RPC custom operation bindings have already been built.");
        }

        foreach (var binding in _bindings.Values)
        {
            registry.EnsureOwnership(binding.RegistryToken);
        }

        _built = true;
        return new A2AJsonRpcCustomOperationBindings(_bindings);
    }

    private void Add(string method, object registryToken, CustomOperationRegistration registration)
    {
        if (_built)
        {
            throw new InvalidOperationException("The JSON-RPC custom operation bindings have already been built.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        if (A2AMethods.IsValidMethod(method))
        {
            throw new InvalidOperationException(
                $"Custom JSON-RPC method '{method}' cannot replace a standard A2A method.");
        }

        if (!_bindings.TryAdd(method, new JsonRpcCustomOperationBinding(registryToken, registration)))
        {
            throw new InvalidOperationException($"Custom JSON-RPC method '{method}' is already mapped.");
        }
    }
}

/// <summary>
/// Immutable JSON-RPC method mappings for custom A2A operations.
/// </summary>
public sealed class A2AJsonRpcCustomOperationBindings
{
    private readonly Dictionary<string, JsonRpcCustomOperationBinding> _bindings;

    internal A2AJsonRpcCustomOperationBindings(
        IReadOnlyDictionary<string, JsonRpcCustomOperationBinding> bindings)
    {
        _bindings = new Dictionary<string, JsonRpcCustomOperationBinding>(
            bindings,
            StringComparer.Ordinal);
    }

    internal bool TryResolve(string method, out JsonRpcCustomOperationBinding binding) =>
        _bindings.TryGetValue(method, out binding!);

    internal bool IsStreamingMethod(string method) =>
        TryResolve(method, out var binding) &&
        binding.Registration.Kind == A2ACustomOperationKind.Streaming;
}

internal sealed record JsonRpcCustomOperationBinding(
    object RegistryToken,
    CustomOperationRegistration Registration);

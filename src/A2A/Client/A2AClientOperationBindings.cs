using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>Creates an HTTP request for a typed A2A operation.</summary>
/// <typeparam name="TRequest">The operation request type.</typeparam>
/// <param name="endpoint">The client endpoint.</param>
/// <param name="request">The typed operation request.</param>
/// <param name="cancellationToken">A cancellation token.</param>
/// <returns>The outbound HTTP request.</returns>
public delegate ValueTask<HttpRequestMessage> A2AHttpClientRequestMapper<TRequest>(
    Uri endpoint,
    TRequest request,
    CancellationToken cancellationToken);

/// <summary>Customizes an outbound request for a typed A2A operation.</summary>
/// <typeparam name="TRequest">The operation request type.</typeparam>
/// <param name="message">The outbound HTTP request.</param>
/// <param name="request">The typed operation request.</param>
public delegate void A2AClientRequestCustomizer<TRequest>(
    HttpRequestMessage message,
    TRequest request);

/// <summary>Builds client transport bindings for custom unary operations.</summary>
public sealed class A2AClientOperationBindingBuilder
{
    private readonly Dictionary<A2AOperationId, object> _registrations = [];

    /// <summary>Maps an operation to a JSON-RPC method.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="operation">The operation definition.</param>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="requestTypeInfo">The request serialization metadata.</param>
    /// <param name="resultTypeInfo">The result serialization metadata.</param>
    /// <param name="requestCustomizer">Optional outbound request customization.</param>
    /// <returns>This builder.</returns>
    public A2AClientOperationBindingBuilder MapJsonRpc<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation,
        string method,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        A2AClientRequestCustomizer<TRequest>? requestCustomizer = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        var registration = GetOrCreate(operation);
        if (registration.JsonRpc is not null)
        {
            throw new InvalidOperationException(
                $"A JSON-RPC client binding is already registered for '{operation.Id.Value}'.");
        }

        registration.JsonRpc = new A2AJsonRpcClientOperationBinding<TRequest, TResult>(
            method,
            requestTypeInfo,
            resultTypeInfo,
            requestCustomizer);
        return this;
    }

    /// <summary>Maps an operation to an HTTP+JSON request.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="operation">The operation definition.</param>
    /// <param name="requestMapper">The outbound HTTP request mapper.</param>
    /// <param name="resultTypeInfo">The result serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AClientOperationBindingBuilder MapHttp<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation,
        A2AHttpClientRequestMapper<TRequest> requestMapper,
        JsonTypeInfo<TResult> resultTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requestMapper);
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        var registration = GetOrCreate(operation);
        if (registration.Http is not null)
        {
            throw new InvalidOperationException(
                $"An HTTP client binding is already registered for '{operation.Id.Value}'.");
        }

        registration.Http = new A2AHttpClientOperationBinding<TRequest, TResult>(
            requestMapper,
            resultTypeInfo);
        return this;
    }

    /// <summary>Builds the immutable client operation bindings.</summary>
    public A2AClientOperationBindings Build()
        => new(_registrations);

    private A2AClientOperationRegistration<TRequest, TResult> GetOrCreate<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation)
    {
        if (!_registrations.TryGetValue(operation.Id, out var value))
        {
            var registration = new A2AClientOperationRegistration<TRequest, TResult>();
            _registrations.Add(operation.Id, registration);
            return registration;
        }

        return value as A2AClientOperationRegistration<TRequest, TResult>
            ?? throw new InvalidOperationException(
                $"Client bindings for '{operation.Id.Value}' use incompatible request or result types.");
    }
}

/// <summary>Contains client transport bindings for custom unary operations.</summary>
public sealed class A2AClientOperationBindings
{
    internal static A2AClientOperationBindings Empty { get; } =
        new(new Dictionary<A2AOperationId, object>());

    private readonly Dictionary<A2AOperationId, object> _registrations;

    internal A2AClientOperationBindings(
        IReadOnlyDictionary<A2AOperationId, object> registrations)
    {
        _registrations = new Dictionary<A2AOperationId, object>(registrations);
    }

    internal A2AJsonRpcClientOperationBinding<TRequest, TResult> GetJsonRpc<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation)
    {
        var registration = GetRegistration(operation);
        return registration.JsonRpc
            ?? throw new InvalidOperationException(
                $"No JSON-RPC client binding is registered for '{operation.Id.Value}'.");
    }

    internal A2AHttpClientOperationBinding<TRequest, TResult> GetHttp<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation)
    {
        var registration = GetRegistration(operation);
        return registration.Http
            ?? throw new InvalidOperationException(
                $"No HTTP client binding is registered for '{operation.Id.Value}'.");
    }

    private A2AClientOperationRegistration<TRequest, TResult> GetRegistration<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (!_registrations.TryGetValue(operation.Id, out var value))
        {
            throw new InvalidOperationException(
                $"No client bindings are registered for '{operation.Id.Value}'.");
        }

        return value as A2AClientOperationRegistration<TRequest, TResult>
            ?? throw new InvalidOperationException(
                $"Client bindings for '{operation.Id.Value}' use incompatible request or result types.");
    }
}

internal sealed class A2AClientOperationRegistration<TRequest, TResult>
{
    internal A2AJsonRpcClientOperationBinding<TRequest, TResult>? JsonRpc { get; set; }

    internal A2AHttpClientOperationBinding<TRequest, TResult>? Http { get; set; }
}

internal sealed record A2AJsonRpcClientOperationBinding<TRequest, TResult>(
    string Method,
    JsonTypeInfo<TRequest> RequestTypeInfo,
    JsonTypeInfo<TResult> ResultTypeInfo,
    A2AClientRequestCustomizer<TRequest>? RequestCustomizer);

internal sealed record A2AHttpClientOperationBinding<TRequest, TResult>(
    A2AHttpClientRequestMapper<TRequest> RequestMapper,
    JsonTypeInfo<TResult> ResultTypeInfo);

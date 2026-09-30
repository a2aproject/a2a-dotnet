using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A.AspNetCore;

internal delegate ValueTask A2AJsonRpcBeforeInvoke(
    A2AOperationContext context,
    CancellationToken cancellationToken);

/// <summary>Builds JSON-RPC bindings for typed unary and streaming operations.</summary>
public sealed class A2AJsonRpcOperationBindingBuilder
{
    private readonly Dictionary<string, IA2AJsonRpcOperationBindingRegistration>
        _bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<object, List<IA2AJsonRpcOperationBindingRegistration>>
        _bindingsByOperation = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, Dictionary<string, IA2AJsonRpcErrorMapping>>
        _errorMappingsByOperation = new(ReferenceEqualityComparer.Instance);

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
        => MapCore(
            method,
            operation,
            requestTypeInfo,
            resultTypeInfo,
            beforeInvoke: null);

    /// <summary>Maps a JSON-RPC method to a typed streaming operation.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="method">The JSON-RPC method name.</param>
    /// <param name="operation">The streaming operation definition.</param>
    /// <param name="requestTypeInfo">Request serialization metadata.</param>
    /// <param name="eventTypeInfo">Event serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AJsonRpcOperationBindingBuilder MapStreaming<TRequest, TEvent>(
        string method,
        A2AStreamingOperation<TRequest, TEvent> operation,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TEvent> eventTypeInfo)
        => MapStreamingCore(
            method,
            operation,
            requestTypeInfo,
            eventTypeInfo,
            beforeInvoke: null);

    /// <summary>Maps a declared unary operation error to a JSON-RPC error code.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <typeparam name="TDetails">The error details type.</typeparam>
    /// <param name="operation">The declaring unary operation.</param>
    /// <param name="error">The declared operation error.</param>
    /// <param name="code">The JSON-RPC error code.</param>
    /// <param name="detailsTypeInfo">Error details serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AJsonRpcOperationBindingBuilder MapError<
        TRequest,
        TResult,
        TDetails>(
        A2AOperation<TRequest, TResult> operation,
        A2AOperationError<TDetails> error,
        int code,
        JsonTypeInfo<TDetails> detailsTypeInfo)
        => MapErrorCore(operation, error, code, detailsTypeInfo);

    /// <summary>Maps a declared streaming operation error to a JSON-RPC error code.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <typeparam name="TDetails">The error details type.</typeparam>
    /// <param name="operation">The declaring streaming operation.</param>
    /// <param name="error">The declared operation error.</param>
    /// <param name="code">The JSON-RPC error code.</param>
    /// <param name="detailsTypeInfo">Error details serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AJsonRpcOperationBindingBuilder MapError<
        TRequest,
        TEvent,
        TDetails>(
        A2AStreamingOperation<TRequest, TEvent> operation,
        A2AOperationError<TDetails> error,
        int code,
        JsonTypeInfo<TDetails> detailsTypeInfo)
        => MapErrorCore(operation, error, code, detailsTypeInfo);

    /// <summary>Builds the immutable JSON-RPC binding table.</summary>
    /// <param name="operationCatalog">The operation-definition catalog.</param>
    /// <returns>The binding table.</returns>
    public A2AJsonRpcOperationBindings Build(
        A2AOperationCatalog operationCatalog)
    {
        ArgumentNullException.ThrowIfNull(operationCatalog);

        return new A2AJsonRpcOperationBindings(
            _bindings.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Build(pair.Key, operationCatalog),
                StringComparer.Ordinal));
    }

    internal A2AJsonRpcOperationBindingBuilder MapCore<TRequest, TResult>(
        string method,
        A2AOperation<TRequest, TResult> operation,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        A2AJsonRpcBeforeInvoke? beforeInvoke)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        AddBinding(
            method,
            operation,
            new A2AJsonRpcUnaryOperationBindingRegistration<TRequest, TResult>(
                operation,
                requestTypeInfo,
                resultTypeInfo,
                beforeInvoke));
        return this;
    }

    internal A2AJsonRpcOperationBindingBuilder MapStreamingCore<TRequest, TEvent>(
        string method,
        A2AStreamingOperation<TRequest, TEvent> operation,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TEvent> eventTypeInfo,
        A2AJsonRpcBeforeInvoke? beforeInvoke)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(eventTypeInfo);

        AddBinding(
            method,
            operation,
            new A2AJsonRpcStreamingOperationBindingRegistration<TRequest, TEvent>(
                operation,
                requestTypeInfo,
                eventTypeInfo,
                beforeInvoke));
        return this;
    }

    private A2AJsonRpcOperationBindingBuilder MapErrorCore<TDetails>(
        object operation,
        A2AOperationError<TDetails> error,
        int code,
        JsonTypeInfo<TDetails> detailsTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(detailsTypeInfo);

        if (!_bindingsByOperation.TryGetValue(operation, out var registrations))
        {
            throw new InvalidOperationException(
                "The A2A operation must have a JSON-RPC binding before its errors can be mapped.");
        }

        if (!_errorMappingsByOperation.TryGetValue(
                operation,
                out var operationMappings))
        {
            operationMappings = new Dictionary<string, IA2AJsonRpcErrorMapping>(
                StringComparer.Ordinal);
            _errorMappingsByOperation.Add(operation, operationMappings);
        }

        var mapping = new A2AJsonRpcErrorMapping<TDetails>(
            error,
            code,
            detailsTypeInfo);
        if (!operationMappings.TryAdd(mapping.ErrorId, mapping))
        {
            throw new InvalidOperationException(
                $"The A2A operation error '{mapping.ErrorId}' already has a JSON-RPC mapping.");
        }

        foreach (var registration in registrations)
        {
            registration.AddErrorMapping(mapping);
        }

        return this;
    }

    private void AddBinding(
        string method,
        object operation,
        IA2AJsonRpcOperationBindingRegistration registration)
    {
        if (!_bindings.TryAdd(method, registration))
        {
            throw new InvalidOperationException(
                $"A JSON-RPC operation binding is already registered for method '{method}'.");
        }

        if (!_bindingsByOperation.TryGetValue(operation, out var registrations))
        {
            registrations = [];
            _bindingsByOperation.Add(operation, registrations);
        }

        registrations.Add(registration);
        if (_errorMappingsByOperation.TryGetValue(
                operation,
                out var operationMappings))
        {
            foreach (var mapping in operationMappings.Values)
            {
                registration.AddErrorMapping(mapping);
            }
        }
    }
}

/// <summary>Resolves JSON-RPC methods to typed unary and streaming operations.</summary>
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

internal interface IA2AJsonRpcOperationBindingRegistration
{
    void AddErrorMapping(IA2AJsonRpcErrorMapping mapping);

    IA2AJsonRpcOperationBinding Build(
        string method,
        A2AOperationCatalog operationCatalog);
}

internal abstract class A2AJsonRpcOperationBindingRegistration
    : IA2AJsonRpcOperationBindingRegistration
{
    private readonly Dictionary<string, IA2AJsonRpcErrorMapping> _errorMappings =
        new(StringComparer.Ordinal);

    protected abstract object Operation { get; }

    protected abstract A2AOperationKind Kind { get; }

    protected abstract Type RequestType { get; }

    protected abstract Type ResponseType { get; }

    public void AddErrorMapping(IA2AJsonRpcErrorMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (!_errorMappings.TryAdd(mapping.ErrorId, mapping))
        {
            throw new InvalidOperationException(
                $"The A2A operation error '{mapping.ErrorId}' already has a JSON-RPC mapping.");
        }
    }

    public IA2AJsonRpcOperationBinding Build(
        string method,
        A2AOperationCatalog operationCatalog)
    {
        if (Operation is not IA2AOperationHandle operationHandle
            || !operationCatalog.TryGetRegistration(
                operationHandle.Id,
                out var operationRegistration)
            || !ReferenceEquals(operationRegistration.Handle, Operation))
        {
            throw new InvalidOperationException(
                "The JSON-RPC binding operation is not defined by the supplied operation catalog.");
        }

        if (operationRegistration.Kind != Kind
            || operationRegistration.RequestType != RequestType
            || operationRegistration.ResponseType != ResponseType)
        {
            throw new InvalidOperationException(
                $"The JSON-RPC binding for '{operationHandle.Id.Value}' uses incompatible operation types.");
        }

        A2AStandardJsonRpcBindingBuilderExtensions.ValidateReservedMethodBinding(
            method,
            operationRegistration);

        foreach (var mapping in _errorMappings.Values)
        {
            if (!operationRegistration.DeclaredErrors.TryGetValue(
                    mapping.ErrorId,
                    out var declaredError)
                || !ReferenceEquals(declaredError, mapping.Error))
            {
                throw new InvalidOperationException(
                    $"The A2A operation '{operationHandle.Id.Value}' does not declare error '{mapping.ErrorId}' with the mapped details type.");
            }
        }

        return BuildCore(
            operationCatalog,
            operationRegistration,
            new Dictionary<string, IA2AJsonRpcErrorMapping>(
                _errorMappings,
                StringComparer.Ordinal));
    }

    protected abstract IA2AJsonRpcOperationBinding BuildCore(
        A2AOperationCatalog operationCatalog,
        A2AOperationRegistration operationRegistration,
        IReadOnlyDictionary<string, IA2AJsonRpcErrorMapping> errorMappings);
}

internal sealed class A2AJsonRpcUnaryOperationBindingRegistration<
    TRequest,
    TResult>(
    A2AOperation<TRequest, TResult> operation,
    JsonTypeInfo<TRequest> requestTypeInfo,
    JsonTypeInfo<TResult> resultTypeInfo,
    A2AJsonRpcBeforeInvoke? beforeInvoke)
    : A2AJsonRpcOperationBindingRegistration
{
    protected override object Operation => operation;

    protected override A2AOperationKind Kind => A2AOperationKind.Unary;

    protected override Type RequestType => typeof(TRequest);

    protected override Type ResponseType => typeof(TResult);

    protected override IA2AJsonRpcOperationBinding BuildCore(
        A2AOperationCatalog operationCatalog,
        A2AOperationRegistration operationRegistration,
        IReadOnlyDictionary<string, IA2AJsonRpcErrorMapping> errorMappings)
        => new A2AJsonRpcUnaryOperationBinding<TRequest, TResult>(
            operationCatalog,
            operationRegistration.Source,
            operation,
            requestTypeInfo,
            resultTypeInfo,
            beforeInvoke,
            errorMappings);
}

internal sealed class A2AJsonRpcStreamingOperationBindingRegistration<
    TRequest,
    TEvent>(
    A2AStreamingOperation<TRequest, TEvent> operation,
    JsonTypeInfo<TRequest> requestTypeInfo,
    JsonTypeInfo<TEvent> eventTypeInfo,
    A2AJsonRpcBeforeInvoke? beforeInvoke)
    : A2AJsonRpcOperationBindingRegistration
{
    protected override object Operation => operation;

    protected override A2AOperationKind Kind => A2AOperationKind.Streaming;

    protected override Type RequestType => typeof(TRequest);

    protected override Type ResponseType => typeof(TEvent);

    protected override IA2AJsonRpcOperationBinding BuildCore(
        A2AOperationCatalog operationCatalog,
        A2AOperationRegistration operationRegistration,
        IReadOnlyDictionary<string, IA2AJsonRpcErrorMapping> errorMappings)
        => new A2AJsonRpcStreamingOperationBinding<TRequest, TEvent>(
            operationCatalog,
            operationRegistration.Source,
            operation,
            requestTypeInfo,
            eventTypeInfo,
            beforeInvoke,
            errorMappings);
}

internal interface IA2AJsonRpcOperationBinding
{
    A2AOperationDiagnosticContext Diagnostics { get; }

    IA2AJsonRpcBoundOperation Bind(JsonElement parameters);
}

internal interface IA2AJsonRpcBoundOperation
{
    ValueTask<IResult> InvokeAsync(
        JsonRpcId requestId,
        A2AOperationContext context,
        A2AOperationHandlerCatalog handlers,
        Activity? operationActivity,
        Activity? transportActivity,
        CancellationToken cancellationToken);

    JsonRpcResponse CreateErrorResponse(
        JsonRpcId requestId,
        Exception exception,
        string internalErrorMessage);
}

internal abstract class A2AJsonRpcOperationBinding(
    IReadOnlyDictionary<string, IA2AJsonRpcErrorMapping> errorMappings)
{
    protected JsonRpcResponse CreateErrorResponse(
        JsonRpcId requestId,
        Exception exception,
        string internalErrorMessage)
    {
        if (exception is A2AException a2aException)
        {
            return JsonRpcResponse.CreateJsonRpcErrorResponse(
                requestId,
                a2aException);
        }

        if (exception is A2AOperationException operationException
            && errorMappings.TryGetValue(
                operationException.ErrorId,
                out var mapping)
            && mapping.TryCreateResponse(
                requestId,
                operationException,
                out var mappedResponse))
        {
            return mappedResponse;
        }

        return JsonRpcResponse.InternalErrorResponse(
            requestId,
            internalErrorMessage);
    }

    protected static TRequest Deserialize<TRequest>(
        JsonElement parameters,
        JsonTypeInfo<TRequest> requestTypeInfo)
    {
        try
        {
            var request = parameters.Deserialize(requestTypeInfo);
            if (request is null)
            {
                throw new A2AException(
                    $"Failed to deserialize parameters as {typeof(TRequest).Name}.",
                    A2AErrorCode.InvalidParams);
            }

            return request;
        }
        catch (JsonException ex)
        {
            throw new A2AException(
                $"Invalid parameters: request body could not be deserialized as {typeof(TRequest).Name}.",
                ex,
                A2AErrorCode.InvalidParams);
        }
    }
}

internal sealed class A2AJsonRpcUnaryOperationBinding<TRequest, TResult>(
    A2AOperationCatalog operationCatalog,
    A2AOperationSource operationSource,
    A2AOperation<TRequest, TResult> operation,
    JsonTypeInfo<TRequest> requestTypeInfo,
    JsonTypeInfo<TResult> resultTypeInfo,
    A2AJsonRpcBeforeInvoke? beforeInvoke,
    IReadOnlyDictionary<string, IA2AJsonRpcErrorMapping> errorMappings)
    : A2AJsonRpcOperationBinding(errorMappings), IA2AJsonRpcOperationBinding
{
    public A2AOperationDiagnosticContext Diagnostics =>
        new(operation.Id, A2AOperationKind.Unary, operationSource);

    public IA2AJsonRpcBoundOperation Bind(JsonElement parameters)
    {
        var request = Deserialize(parameters, requestTypeInfo);
        operationCatalog.Validate(operation, request);
        return new BoundOperation(
            operation,
            request,
            resultTypeInfo,
            beforeInvoke,
            CreateErrorResponse);
    }

    private sealed class BoundOperation(
        A2AOperation<TRequest, TResult> operation,
        TRequest request,
        JsonTypeInfo<TResult> resultTypeInfo,
        A2AJsonRpcBeforeInvoke? beforeInvoke,
        Func<JsonRpcId, Exception, string, JsonRpcResponse> createErrorResponse)
        : IA2AJsonRpcBoundOperation
    {
        public async ValueTask<IResult> InvokeAsync(
            JsonRpcId requestId,
            A2AOperationContext context,
            A2AOperationHandlerCatalog handlers,
            Activity? operationActivity,
            Activity? transportActivity,
            CancellationToken cancellationToken)
        {
            if (beforeInvoke is not null)
            {
                await beforeInvoke(context, cancellationToken)
                    .ConfigureAwait(false);
            }

            var result = await handlers.InvokeAsync(
                operation,
                context,
                request,
                cancellationToken).ConfigureAwait(false);

            var response = result is A2AEmptyResult
                ? JsonRpcResponse.CreateJsonRpcResponse<object?>(
                    requestId,
                    null)
                : JsonRpcResponse.CreateJsonRpcResponse(
                    requestId,
                    result,
                    resultTypeInfo);
            return new JsonRpcResponseResult(response);
        }

        public JsonRpcResponse CreateErrorResponse(
            JsonRpcId requestId,
            Exception exception,
            string internalErrorMessage)
            => createErrorResponse(
                requestId,
                exception,
                internalErrorMessage);
    }
}

internal sealed class A2AJsonRpcStreamingOperationBinding<TRequest, TEvent>(
    A2AOperationCatalog operationCatalog,
    A2AOperationSource operationSource,
    A2AStreamingOperation<TRequest, TEvent> operation,
    JsonTypeInfo<TRequest> requestTypeInfo,
    JsonTypeInfo<TEvent> eventTypeInfo,
    A2AJsonRpcBeforeInvoke? beforeInvoke,
    IReadOnlyDictionary<string, IA2AJsonRpcErrorMapping> errorMappings)
    : A2AJsonRpcOperationBinding(errorMappings), IA2AJsonRpcOperationBinding
{
    public A2AOperationDiagnosticContext Diagnostics =>
        new(operation.Id, A2AOperationKind.Streaming, operationSource);

    public IA2AJsonRpcBoundOperation Bind(JsonElement parameters)
    {
        var request = Deserialize(parameters, requestTypeInfo);
        operationCatalog.ValidateStreaming(operation, request);
        return new BoundOperation(
            operation,
            request,
            eventTypeInfo,
            beforeInvoke,
            CreateErrorResponse);
    }

    private sealed class BoundOperation(
        A2AStreamingOperation<TRequest, TEvent> operation,
        TRequest request,
        JsonTypeInfo<TEvent> eventTypeInfo,
        A2AJsonRpcBeforeInvoke? beforeInvoke,
        Func<JsonRpcId, Exception, string, JsonRpcResponse> createErrorResponse)
        : IA2AJsonRpcBoundOperation
    {
        public async ValueTask<IResult> InvokeAsync(
            JsonRpcId requestId,
            A2AOperationContext context,
            A2AOperationHandlerCatalog handlers,
            Activity? operationActivity,
            Activity? transportActivity,
            CancellationToken cancellationToken)
        {
            if (beforeInvoke is not null)
            {
                await beforeInvoke(context, cancellationToken)
                    .ConfigureAwait(false);
            }

            var events = handlers.InvokeStreamingAsync(
                operation,
                context,
                request,
                cancellationToken);
            return JsonRpcStreamedResult.Create(
                events,
                requestId,
                eventTypeInfo,
                createErrorResponse,
                operationActivity,
                transportActivity);
        }

        public JsonRpcResponse CreateErrorResponse(
            JsonRpcId requestId,
            Exception exception,
            string internalErrorMessage)
            => createErrorResponse(
                requestId,
                exception,
                internalErrorMessage);
    }
}

internal interface IA2AJsonRpcErrorMapping
{
    string ErrorId { get; }

    object Error { get; }

    bool TryCreateResponse(
        JsonRpcId requestId,
        A2AOperationException exception,
        out JsonRpcResponse response);
}

internal sealed class A2AJsonRpcErrorMapping<TDetails>(
    A2AOperationError<TDetails> error,
    int code,
    JsonTypeInfo<TDetails> detailsTypeInfo)
    : IA2AJsonRpcErrorMapping
{
    public string ErrorId => error.ErrorId;

    public object Error => error;

    public bool TryCreateResponse(
        JsonRpcId requestId,
        A2AOperationException exception,
        out JsonRpcResponse response)
    {
        if (exception is not A2AOperationException<TDetails> typedException
            || !ReferenceEquals(typedException.Error, error))
        {
            response = null!;
            return false;
        }

        try
        {
            response = new JsonRpcResponse
            {
                Id = requestId,
                Error = new JsonRpcError
                {
                    Code = code,
                    Message = typedException.Message,
                    Data = JsonSerializer.SerializeToElement(
                        typedException.Details,
                        detailsTypeInfo),
                },
            };
            return true;
        }
        catch (Exception)
        {
            response = null!;
            return false;
        }
    }
}

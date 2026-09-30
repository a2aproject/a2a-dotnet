using System.Text.Json;
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

/// <summary>Builds client transport bindings for unary and streaming operations.</summary>
public sealed class A2AClientOperationBindingBuilder
{
    private readonly Dictionary<A2AOperationId, IA2AClientOperationRegistrationBuilder>
        _registrations = [];
    private A2AStandardOperations? _standardOperations;

    /// <summary>Maps a unary operation to a JSON-RPC method.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="operation">The unary operation.</param>
    /// <param name="method">The JSON-RPC method name.</param>
    /// <param name="requestTypeInfo">Request serialization metadata.</param>
    /// <param name="resultTypeInfo">Result serialization metadata.</param>
    /// <param name="requestCustomizer">Optional request customization.</param>
    /// <returns>This builder.</returns>
    public A2AClientOperationBindingBuilder MapJsonRpc<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation,
        string method,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        A2AClientRequestCustomizer<TRequest>? requestCustomizer = null)
        => MapJsonRpcCore(
            operation,
            method,
            requestTypeInfo,
            resultTypeInfo,
            requestCustomizer,
            isCanonicalStandardBinding: false);

    /// <summary>Maps a streaming operation to a JSON-RPC method.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="operation">The streaming operation.</param>
    /// <param name="method">The JSON-RPC method name.</param>
    /// <param name="requestTypeInfo">Request serialization metadata.</param>
    /// <param name="eventTypeInfo">Event serialization metadata.</param>
    /// <param name="requestCustomizer">Optional request customization.</param>
    /// <returns>This builder.</returns>
    public A2AClientOperationBindingBuilder MapJsonRpcStreaming<TRequest, TEvent>(
        A2AStreamingOperation<TRequest, TEvent> operation,
        string method,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TEvent> eventTypeInfo,
        A2AClientRequestCustomizer<TRequest>? requestCustomizer = null)
        => MapJsonRpcStreamingCore(
            operation,
            method,
            requestTypeInfo,
            eventTypeInfo,
            requestCustomizer,
            isCanonicalStandardBinding: false);

    /// <summary>Maps a unary operation to an HTTP+JSON request.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="operation">The unary operation.</param>
    /// <param name="requestMapper">The outbound request mapper.</param>
    /// <param name="resultTypeInfo">Result serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AClientOperationBindingBuilder MapHttp<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation,
        A2AHttpClientRequestMapper<TRequest> requestMapper,
        JsonTypeInfo<TResult> resultTypeInfo)
        => MapHttpCore(
            operation,
            requestMapper,
            resultTypeInfo,
            operation.Id.Value,
            isCanonicalStandardBinding: false);

    /// <summary>Maps a streaming operation to an HTTP+JSON request.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="operation">The streaming operation.</param>
    /// <param name="requestMapper">The outbound request mapper.</param>
    /// <param name="eventTypeInfo">Event serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AClientOperationBindingBuilder MapHttpStreaming<TRequest, TEvent>(
        A2AStreamingOperation<TRequest, TEvent> operation,
        A2AHttpClientRequestMapper<TRequest> requestMapper,
        JsonTypeInfo<TEvent> eventTypeInfo)
        => MapHttpStreamingCore(
            operation,
            requestMapper,
            eventTypeInfo,
            operation.Id.Value,
            isCanonicalStandardBinding: false);

    /// <summary>Maps a declared unary operation error to a JSON-RPC error code.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <typeparam name="TDetails">The error details type.</typeparam>
    /// <param name="operation">The declaring operation.</param>
    /// <param name="error">The declared error.</param>
    /// <param name="code">The JSON-RPC error code.</param>
    /// <param name="detailsTypeInfo">Error details serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AClientOperationBindingBuilder MapJsonRpcError<
        TRequest,
        TResult,
        TDetails>(
        A2AOperation<TRequest, TResult> operation,
        A2AOperationError<TDetails> error,
        int code,
        JsonTypeInfo<TDetails> detailsTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(detailsTypeInfo);

        var registration = GetUnaryRegistration(operation, requireExisting: true);
        var binding = registration.JsonRpc
            ?? throw new InvalidOperationException(
                "The A2A operation must have a JSON-RPC client binding before its errors can be mapped.");
        binding.AddErrorMapping(error, code, detailsTypeInfo);
        return this;
    }

    /// <summary>Maps a declared streaming operation error to a JSON-RPC error code.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <typeparam name="TDetails">The error details type.</typeparam>
    /// <param name="operation">The declaring streaming operation.</param>
    /// <param name="error">The declared error.</param>
    /// <param name="code">The JSON-RPC error code.</param>
    /// <param name="detailsTypeInfo">Error details serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AClientOperationBindingBuilder MapJsonRpcError<
        TRequest,
        TEvent,
        TDetails>(
        A2AStreamingOperation<TRequest, TEvent> operation,
        A2AOperationError<TDetails> error,
        int code,
        JsonTypeInfo<TDetails> detailsTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(detailsTypeInfo);

        var registration = GetStreamingRegistration(
            operation,
            requireExisting: true);
        var binding = registration.JsonRpc
            ?? throw new InvalidOperationException(
                "The A2A operation must have a JSON-RPC client binding before its errors can be mapped.");
        binding.AddErrorMapping(error, code, detailsTypeInfo);
        return this;
    }

    /// <summary>Maps a declared unary operation error to an HTTP status.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <typeparam name="TDetails">The error details type.</typeparam>
    /// <param name="operation">The declaring operation.</param>
    /// <param name="error">The declared error.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="detailsTypeInfo">Error details serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AClientOperationBindingBuilder MapHttpError<
        TRequest,
        TResult,
        TDetails>(
        A2AOperation<TRequest, TResult> operation,
        A2AOperationError<TDetails> error,
        int statusCode,
        JsonTypeInfo<TDetails> detailsTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(detailsTypeInfo);
        ValidateHttpErrorStatus(statusCode);

        var registration = GetUnaryRegistration(operation, requireExisting: true);
        var binding = registration.Http
            ?? throw new InvalidOperationException(
                "The A2A operation must have an HTTP client binding before its errors can be mapped.");
        binding.AddErrorMapping(error, statusCode, detailsTypeInfo);
        return this;
    }

    /// <summary>Maps a declared streaming operation error to an HTTP status.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <typeparam name="TDetails">The error details type.</typeparam>
    /// <param name="operation">The declaring streaming operation.</param>
    /// <param name="error">The declared error.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="detailsTypeInfo">Error details serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AClientOperationBindingBuilder MapHttpError<
        TRequest,
        TEvent,
        TDetails>(
        A2AStreamingOperation<TRequest, TEvent> operation,
        A2AOperationError<TDetails> error,
        int statusCode,
        JsonTypeInfo<TDetails> detailsTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(detailsTypeInfo);
        ValidateHttpErrorStatus(statusCode);

        var registration = GetStreamingRegistration(
            operation,
            requireExisting: true);
        var binding = registration.Http
            ?? throw new InvalidOperationException(
                "The A2A operation must have an HTTP client binding before its errors can be mapped.");
        binding.AddErrorMapping(error, statusCode, detailsTypeInfo);
        return this;
    }

    /// <summary>
    /// Builds immutable client operation bindings without catalog validation.
    /// </summary>
    /// <remarks>
    /// This overload preserves the original custom-operation API. Use
    /// <see cref="Build(A2AOperationCatalog)"/> to enable catalog membership,
    /// semantic validation, and declared-error validation.
    /// </remarks>
    public A2AClientOperationBindings Build()
    {
        ValidateCatalogFreeBuild();
        return BuildCore(operationCatalog: null);
    }

    /// <summary>Builds immutable client operation bindings for a catalog.</summary>
    /// <param name="operationCatalog">The operation catalog.</param>
    /// <returns>The immutable client operation bindings.</returns>
    public A2AClientOperationBindings Build(
        A2AOperationCatalog operationCatalog)
    {
        ArgumentNullException.ThrowIfNull(operationCatalog);
        return BuildCore(operationCatalog);
    }

    internal A2AClientOperationBindingBuilder MapJsonRpcCore<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation,
        string method,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        A2AClientRequestCustomizer<TRequest>? requestCustomizer,
        bool isCanonicalStandardBinding)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        var registration = GetUnaryRegistration(operation);
        if (registration.JsonRpc is not null)
        {
            throw new InvalidOperationException(
                $"A JSON-RPC client binding is already registered for '{operation.Id.Value}'.");
        }

        registration.JsonRpc =
            new A2AJsonRpcClientOperationBindingBuilder<TRequest, TResult>(
                method,
                requestTypeInfo,
                resultTypeInfo,
                requestCustomizer,
                isCanonicalStandardBinding);
        return this;
    }

    internal A2AClientOperationBindingBuilder MapJsonRpcStreamingCore<
        TRequest,
        TEvent>(
        A2AStreamingOperation<TRequest, TEvent> operation,
        string method,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TEvent> eventTypeInfo,
        A2AClientRequestCustomizer<TRequest>? requestCustomizer,
        bool isCanonicalStandardBinding)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(eventTypeInfo);

        var registration = GetStreamingRegistration(operation);
        if (registration.JsonRpc is not null)
        {
            throw new InvalidOperationException(
                $"A JSON-RPC client binding is already registered for '{operation.Id.Value}'.");
        }

        registration.JsonRpc =
            new A2AJsonRpcClientOperationBindingBuilder<TRequest, TEvent>(
                method,
                requestTypeInfo,
                eventTypeInfo,
                requestCustomizer,
                isCanonicalStandardBinding);
        return this;
    }

    internal A2AClientOperationBindingBuilder MapHttpCore<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation,
        A2AHttpClientRequestMapper<TRequest> requestMapper,
        JsonTypeInfo<TResult> resultTypeInfo,
        string operationName,
        bool isCanonicalStandardBinding)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requestMapper);
        ArgumentNullException.ThrowIfNull(resultTypeInfo);
        ArgumentException.ThrowIfNullOrEmpty(operationName);

        var registration = GetUnaryRegistration(operation);
        if (registration.Http is not null)
        {
            throw new InvalidOperationException(
                $"An HTTP client binding is already registered for '{operation.Id.Value}'.");
        }

        registration.Http =
            new A2AHttpClientOperationBindingBuilder<TRequest, TResult>(
                requestMapper,
                resultTypeInfo,
                operationName,
                isCanonicalStandardBinding);
        return this;
    }

    internal A2AClientOperationBindingBuilder MapHttpStreamingCore<
        TRequest,
        TEvent>(
        A2AStreamingOperation<TRequest, TEvent> operation,
        A2AHttpClientRequestMapper<TRequest> requestMapper,
        JsonTypeInfo<TEvent> eventTypeInfo,
        string operationName,
        bool isCanonicalStandardBinding)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requestMapper);
        ArgumentNullException.ThrowIfNull(eventTypeInfo);
        ArgumentException.ThrowIfNullOrEmpty(operationName);

        var registration = GetStreamingRegistration(operation);
        if (registration.Http is not null)
        {
            throw new InvalidOperationException(
                $"An HTTP client binding is already registered for '{operation.Id.Value}'.");
        }

        registration.Http =
            new A2AHttpClientOperationBindingBuilder<TRequest, TEvent>(
                requestMapper,
                eventTypeInfo,
                operationName,
                isCanonicalStandardBinding);
        return this;
    }

    internal void SetStandardOperations(A2AStandardOperations standard)
    {
        ArgumentNullException.ThrowIfNull(standard);
        if (_standardOperations is not null
            && !ReferenceEquals(_standardOperations, standard))
        {
            throw new InvalidOperationException(
                "Standard client bindings must use one standard operation set.");
        }

        _standardOperations = standard;
    }

    private A2AClientOperationBindings BuildCore(
        A2AOperationCatalog? operationCatalog)
        => new(
            _registrations.ToDictionary(
                static pair => pair.Key,
                pair => pair.Value.Build(operationCatalog)),
            _standardOperations);

    private void ValidateCatalogFreeBuild()
    {
        if (_standardOperations is not null)
        {
            throw new InvalidOperationException(
                "Standard client bindings require operation catalog validation. Call Build(A2AOperationCatalog).");
        }

        foreach (var registration in _registrations.Values)
        {
            registration.ValidateCatalogFreeBuild();
        }
    }

    private A2AClientUnaryOperationRegistrationBuilder<TRequest, TResult>
        GetUnaryRegistration<TRequest, TResult>(
            A2AOperation<TRequest, TResult> operation,
            bool requireExisting = false)
    {
        if (!_registrations.TryGetValue(operation.Id, out var value))
        {
            if (requireExisting)
            {
                throw new InvalidOperationException(
                    $"No client bindings are registered for '{operation.Id.Value}'.");
            }

            var registration =
                new A2AClientUnaryOperationRegistrationBuilder<TRequest, TResult>(
                    operation);
            _registrations.Add(operation.Id, registration);
            return registration;
        }

        return value as
                A2AClientUnaryOperationRegistrationBuilder<TRequest, TResult>
            ?? throw new InvalidOperationException(
                $"Client bindings for '{operation.Id.Value}' use incompatible operation kind or types.");
    }

    private A2AClientStreamingOperationRegistrationBuilder<TRequest, TEvent>
        GetStreamingRegistration<TRequest, TEvent>(
            A2AStreamingOperation<TRequest, TEvent> operation,
            bool requireExisting = false)
    {
        if (!_registrations.TryGetValue(operation.Id, out var value))
        {
            if (requireExisting)
            {
                throw new InvalidOperationException(
                    $"No client bindings are registered for '{operation.Id.Value}'.");
            }

            var registration =
                new A2AClientStreamingOperationRegistrationBuilder<
                    TRequest,
                    TEvent>(operation);
            _registrations.Add(operation.Id, registration);
            return registration;
        }

        return value as
                A2AClientStreamingOperationRegistrationBuilder<TRequest, TEvent>
            ?? throw new InvalidOperationException(
                $"Client bindings for '{operation.Id.Value}' use incompatible operation kind or types.");
    }

    private static void ValidateHttpErrorStatus(int statusCode)
    {
        if (statusCode is < 400 or > 599)
        {
            throw new ArgumentOutOfRangeException(
                nameof(statusCode),
                statusCode,
                "An HTTP operation error status must be between 400 and 599.");
        }
    }
}

/// <summary>Contains immutable client transport bindings.</summary>
public sealed class A2AClientOperationBindings
{
    internal static A2AClientOperationBindings Empty { get; } =
        new(
            new Dictionary<A2AOperationId, IA2AClientOperationRegistration>(),
            standardOperations: null);

    private readonly Dictionary<
        A2AOperationId,
        IA2AClientOperationRegistration> _registrations;

    internal A2AClientOperationBindings(
        IReadOnlyDictionary<
            A2AOperationId,
            IA2AClientOperationRegistration> registrations,
        A2AStandardOperations? standardOperations)
    {
        _registrations = new Dictionary<
            A2AOperationId,
            IA2AClientOperationRegistration>(registrations);
        StandardOperations = standardOperations;
    }

    internal A2AStandardOperations? StandardOperations { get; }

    internal A2AOperationSource GetSource(A2AOperationId id) =>
        _registrations.TryGetValue(id, out var registration)
            ? registration.Source
            : A2AOperationSource.Extension;

    internal A2AJsonRpcClientOperationBinding<TRequest, TResult> GetJsonRpc<
        TRequest,
        TResult>(
        A2AOperation<TRequest, TResult> operation)
    {
        var registration = GetUnaryRegistration(operation);
        return registration.JsonRpc
            ?? throw new InvalidOperationException(
                $"No JSON-RPC client binding is registered for '{operation.Id.Value}'.");
    }

    internal A2AJsonRpcClientOperationBinding<TRequest, TEvent>
        GetJsonRpcStreaming<TRequest, TEvent>(
            A2AStreamingOperation<TRequest, TEvent> operation)
    {
        var registration = GetStreamingRegistration(operation);
        return registration.JsonRpc
            ?? throw new InvalidOperationException(
                $"No JSON-RPC client binding is registered for '{operation.Id.Value}'.");
    }

    internal A2AHttpClientOperationBinding<TRequest, TResult> GetHttp<
        TRequest,
        TResult>(
        A2AOperation<TRequest, TResult> operation)
    {
        var registration = GetUnaryRegistration(operation);
        return registration.Http
            ?? throw new InvalidOperationException(
                $"No HTTP client binding is registered for '{operation.Id.Value}'.");
    }

    internal A2AHttpClientOperationBinding<TRequest, TEvent> GetHttpStreaming<
        TRequest,
        TEvent>(
        A2AStreamingOperation<TRequest, TEvent> operation)
    {
        var registration = GetStreamingRegistration(operation);
        return registration.Http
            ?? throw new InvalidOperationException(
                $"No HTTP client binding is registered for '{operation.Id.Value}'.");
    }

    internal static A2AClientOperationBindings Combine(
        A2AClientOperationBindings first,
        A2AClientOperationBindings second)
    {
        var registrations = new Dictionary<
            A2AOperationId,
            IA2AClientOperationRegistration>(first._registrations);
        foreach (var pair in second._registrations)
        {
            if (!registrations.TryAdd(pair.Key, pair.Value))
            {
                throw new InvalidOperationException(
                    $"Client bindings are already registered for '{pair.Key.Value}'. Standard client bindings cannot be replaced.");
            }
        }

        return new A2AClientOperationBindings(
            registrations,
            second.StandardOperations ?? first.StandardOperations);
    }

    private A2AClientUnaryOperationRegistration<TRequest, TResult>
        GetUnaryRegistration<TRequest, TResult>(
            A2AOperation<TRequest, TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!_registrations.TryGetValue(operation.Id, out var value))
        {
            throw new InvalidOperationException(
                $"No client bindings are registered for '{operation.Id.Value}'.");
        }

        if (value is not
            A2AClientUnaryOperationRegistration<TRequest, TResult> registration)
        {
            throw new InvalidOperationException(
                $"Client bindings for '{operation.Id.Value}' use incompatible operation kind or types.");
        }

        if (!ReferenceEquals(registration.Operation, operation))
        {
            throw new InvalidOperationException(
                $"The A2A operation '{operation.Id.Value}' does not belong to the configured client bindings.");
        }

        return registration;
    }

    private A2AClientStreamingOperationRegistration<TRequest, TEvent>
        GetStreamingRegistration<TRequest, TEvent>(
            A2AStreamingOperation<TRequest, TEvent> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!_registrations.TryGetValue(operation.Id, out var value))
        {
            throw new InvalidOperationException(
                $"No client bindings are registered for '{operation.Id.Value}'.");
        }

        if (value is not
            A2AClientStreamingOperationRegistration<TRequest, TEvent>
                registration)
        {
            throw new InvalidOperationException(
                $"Client bindings for '{operation.Id.Value}' use incompatible operation kind or types.");
        }

        if (!ReferenceEquals(registration.Operation, operation))
        {
            throw new InvalidOperationException(
                $"The A2A operation '{operation.Id.Value}' does not belong to the configured client bindings.");
        }

        return registration;
    }
}

internal interface IA2AClientOperationRegistrationBuilder
{
    void ValidateCatalogFreeBuild();

    IA2AClientOperationRegistration Build(
        A2AOperationCatalog? operationCatalog);
}

internal interface IA2AClientOperationRegistration
{
    A2AOperationSource Source { get; }
}

internal sealed class A2AClientUnaryOperationRegistrationBuilder<
    TRequest,
    TResult>(
    A2AOperation<TRequest, TResult> operation)
    : IA2AClientOperationRegistrationBuilder
{
    internal A2AJsonRpcClientOperationBindingBuilder<TRequest, TResult>?
        JsonRpc
    { get; set; }

    internal A2AHttpClientOperationBindingBuilder<TRequest, TResult>?
        Http
    { get; set; }

    public void ValidateCatalogFreeBuild()
    {
        if (JsonRpc?.HasErrorMappings == true
            || Http?.HasErrorMappings == true)
        {
            throw new InvalidOperationException(
                $"Client error mappings for '{operation.Id.Value}' require operation catalog validation. Call Build(A2AOperationCatalog).");
        }

        if (operation.RequiresCatalog)
        {
            throw new InvalidOperationException(
                $"The client binding operation '{operation.Id.Value}' was defined by an operation catalog. Call Build(A2AOperationCatalog).");
        }
    }

    public IA2AClientOperationRegistration Build(
        A2AOperationCatalog? operationCatalog)
    {
        var operationRegistration = ValidateOperation(
            operationCatalog,
            operation,
            A2AOperationKind.Unary,
            typeof(TRequest),
            typeof(TResult));
        ValidateStandardBindings(operationRegistration, JsonRpc, Http);
        return new A2AClientUnaryOperationRegistration<TRequest, TResult>(
            operation,
            operationRegistration?.Source ?? A2AOperationSource.Extension,
            JsonRpc?.Build(
                operationCatalog is null
                    ? null
                    : request => operationCatalog.Validate(operation, request),
                operationRegistration),
            Http?.Build(
                operationCatalog is null
                    ? null
                    : request => operationCatalog.Validate(operation, request),
                operationRegistration));
    }

    private static A2AOperationRegistration? ValidateOperation(
        A2AOperationCatalog? operationCatalog,
        A2AOperation<TRequest, TResult> operation,
        A2AOperationKind kind,
        Type requestType,
        Type responseType)
    {
        if (operationCatalog is null)
        {
            return null;
        }

        if (!operationCatalog.TryGetRegistration(
                operation.Id,
                out var registration)
            || !ReferenceEquals(registration.Handle, operation))
        {
            throw new InvalidOperationException(
                $"The client binding operation '{operation.Id.Value}' is not defined by the supplied operation catalog.");
        }

        if (registration.Kind != kind
            || registration.RequestType != requestType
            || registration.ResponseType != responseType)
        {
            throw new InvalidOperationException(
                $"Client bindings for '{operation.Id.Value}' use incompatible operation kind or types.");
        }

        return registration;
    }

    private static void ValidateStandardBindings(
        A2AOperationRegistration? registration,
        A2AJsonRpcClientOperationBindingBuilder<TRequest, TResult>? jsonRpc,
        A2AHttpClientOperationBindingBuilder<TRequest, TResult>? http)
    {
        if (registration?.Source != A2AOperationSource.Standard)
        {
            return;
        }

        if (jsonRpc is not null && !jsonRpc.IsCanonicalStandardBinding
            || http is not null && !http.IsCanonicalStandardBinding)
        {
            throw new InvalidOperationException(
                $"The standard A2A operation '{registration.Id.Value}' must use the canonical standard client binding contributor.");
        }
    }
}

internal sealed class A2AClientStreamingOperationRegistrationBuilder<
    TRequest,
    TEvent>(
    A2AStreamingOperation<TRequest, TEvent> operation)
    : IA2AClientOperationRegistrationBuilder
{
    internal A2AJsonRpcClientOperationBindingBuilder<TRequest, TEvent>?
        JsonRpc
    { get; set; }

    internal A2AHttpClientOperationBindingBuilder<TRequest, TEvent>?
        Http
    { get; set; }

    public void ValidateCatalogFreeBuild()
        => throw new InvalidOperationException(
            $"Streaming client bindings for '{operation.Id.Value}' require operation catalog validation. Call Build(A2AOperationCatalog).");

    public IA2AClientOperationRegistration Build(
        A2AOperationCatalog? operationCatalog)
    {
        A2AOperationRegistration? operationRegistration = null;
        if (operationCatalog is not null)
        {
            if (!operationCatalog.TryGetRegistration(
                    operation.Id,
                    out operationRegistration)
                || !ReferenceEquals(operationRegistration.Handle, operation))
            {
                throw new InvalidOperationException(
                    $"The client binding operation '{operation.Id.Value}' is not defined by the supplied operation catalog.");
            }

            if (operationRegistration.Kind != A2AOperationKind.Streaming
                || operationRegistration.RequestType != typeof(TRequest)
                || operationRegistration.ResponseType != typeof(TEvent))
            {
                throw new InvalidOperationException(
                    $"Client bindings for '{operation.Id.Value}' use incompatible operation kind or types.");
            }
        }

        if (operationRegistration?.Source == A2AOperationSource.Standard
            && (JsonRpc is not null
                    && !JsonRpc.IsCanonicalStandardBinding
                || Http is not null
                    && !Http.IsCanonicalStandardBinding))
        {
            throw new InvalidOperationException(
                $"The standard A2A operation '{operation.Id.Value}' must use the canonical standard client binding contributor.");
        }

        return new A2AClientStreamingOperationRegistration<TRequest, TEvent>(
            operation,
            operationRegistration?.Source ?? A2AOperationSource.Extension,
            JsonRpc?.Build(
                operationCatalog is null
                    ? null
                    : request => operationCatalog.ValidateStreaming(
                        operation,
                        request),
                operationRegistration),
            Http?.Build(
                operationCatalog is null
                    ? null
                    : request => operationCatalog.ValidateStreaming(
                        operation,
                        request),
                operationRegistration));
    }
}

internal sealed record A2AClientUnaryOperationRegistration<TRequest, TResult>(
    A2AOperation<TRequest, TResult> Operation,
    A2AOperationSource Source,
    A2AJsonRpcClientOperationBinding<TRequest, TResult>? JsonRpc,
    A2AHttpClientOperationBinding<TRequest, TResult>? Http)
    : IA2AClientOperationRegistration;

internal sealed record A2AClientStreamingOperationRegistration<TRequest, TEvent>(
    A2AStreamingOperation<TRequest, TEvent> Operation,
    A2AOperationSource Source,
    A2AJsonRpcClientOperationBinding<TRequest, TEvent>? JsonRpc,
    A2AHttpClientOperationBinding<TRequest, TEvent>? Http)
    : IA2AClientOperationRegistration;

internal sealed class A2AJsonRpcClientOperationBindingBuilder<
    TRequest,
    TResponse>(
    string method,
    JsonTypeInfo<TRequest> requestTypeInfo,
    JsonTypeInfo<TResponse> responseTypeInfo,
    A2AClientRequestCustomizer<TRequest>? requestCustomizer,
    bool isCanonicalStandardBinding)
{
    private readonly Dictionary<int, IA2AJsonRpcClientErrorMapping>
        _errorMappings = [];

    internal bool HasErrorMappings => _errorMappings.Count != 0;

    internal bool IsCanonicalStandardBinding => isCanonicalStandardBinding;

    internal void AddErrorMapping<TDetails>(
        A2AOperationError<TDetails> error,
        int code,
        JsonTypeInfo<TDetails> detailsTypeInfo)
    {
        if (!_errorMappings.TryAdd(
                code,
                new A2AJsonRpcClientErrorMapping<TDetails>(
                    error,
                    code,
                    detailsTypeInfo)))
        {
            throw new InvalidOperationException(
                $"A JSON-RPC client error mapping is already registered for code '{code}'.");
        }
    }

    internal A2AJsonRpcClientOperationBinding<TRequest, TResponse> Build(
        Action<TRequest>? validator,
        A2AOperationRegistration? operationRegistration)
    {
        A2AClientOperationBindingValidation.ValidateErrorMappings(
            operationRegistration,
            _errorMappings.Values);
        return new(
            method,
            requestTypeInfo,
            responseTypeInfo,
            requestCustomizer,
            validator,
            new Dictionary<int, IA2AJsonRpcClientErrorMapping>(
                _errorMappings));
    }
}

internal sealed class A2AHttpClientOperationBindingBuilder<
    TRequest,
    TResponse>(
    A2AHttpClientRequestMapper<TRequest> requestMapper,
    JsonTypeInfo<TResponse> responseTypeInfo,
    string operationName,
    bool isCanonicalStandardBinding)
{
    private readonly Dictionary<
        A2AHttpClientErrorMappingKey,
        IA2AHttpClientErrorMapping> _errorMappings = [];

    internal bool HasErrorMappings => _errorMappings.Count != 0;

    internal bool IsCanonicalStandardBinding => isCanonicalStandardBinding;

    internal void AddErrorMapping<TDetails>(
        A2AOperationError<TDetails> error,
        int statusCode,
        JsonTypeInfo<TDetails> detailsTypeInfo)
    {
        var key = new A2AHttpClientErrorMappingKey(
            statusCode,
            error.ErrorId);
        if (!_errorMappings.TryAdd(
                key,
                new A2AHttpClientErrorMapping<TDetails>(
                    error,
                    statusCode,
                    detailsTypeInfo)))
        {
            throw new InvalidOperationException(
                $"An HTTP client error mapping is already registered for status '{statusCode}' and error '{error.ErrorId}'.");
        }
    }

    internal A2AHttpClientOperationBinding<TRequest, TResponse> Build(
        Action<TRequest>? validator,
        A2AOperationRegistration? operationRegistration)
    {
        A2AClientOperationBindingValidation.ValidateErrorMappings(
            operationRegistration,
            _errorMappings.Values);
        return new(
            requestMapper,
            responseTypeInfo,
            operationName,
            validator,
            new Dictionary<
                A2AHttpClientErrorMappingKey,
                IA2AHttpClientErrorMapping>(_errorMappings));
    }
}

internal sealed class A2AJsonRpcClientOperationBinding<TRequest, TResponse>(
    string method,
    JsonTypeInfo<TRequest> requestTypeInfo,
    JsonTypeInfo<TResponse> responseTypeInfo,
    A2AClientRequestCustomizer<TRequest>? requestCustomizer,
    Action<TRequest>? validator,
    IReadOnlyDictionary<int, IA2AJsonRpcClientErrorMapping> errorMappings)
{
    internal string Method { get; } = method;

    internal JsonTypeInfo<TRequest> RequestTypeInfo { get; } = requestTypeInfo;

    internal JsonTypeInfo<TResponse> ResponseTypeInfo { get; } =
        responseTypeInfo;

    internal A2AClientRequestCustomizer<TRequest>? RequestCustomizer { get; } =
        requestCustomizer;

    internal void Validate(TRequest request) => validator?.Invoke(request);

    internal bool TryCreateOperationException(
        JsonRpcError error,
        out A2AOperationException exception)
    {
        if (errorMappings.TryGetValue(error.Code, out var mapping))
        {
            return mapping.TryCreateException(error, out exception);
        }

        exception = null!;
        return false;
    }
}

internal sealed class A2AHttpClientOperationBinding<TRequest, TResponse>(
    A2AHttpClientRequestMapper<TRequest> requestMapper,
    JsonTypeInfo<TResponse> responseTypeInfo,
    string operationName,
    Action<TRequest>? validator,
    IReadOnlyDictionary<
        A2AHttpClientErrorMappingKey,
        IA2AHttpClientErrorMapping> errorMappings)
{
    internal A2AHttpClientRequestMapper<TRequest> RequestMapper { get; } =
        requestMapper;

    internal JsonTypeInfo<TResponse> ResponseTypeInfo { get; } =
        responseTypeInfo;

    internal string OperationName { get; } = operationName;

    internal void Validate(TRequest request) => validator?.Invoke(request);

    internal bool TryCreateOperationException(
        int statusCode,
        string errorId,
        string message,
        JsonElement details,
        out A2AOperationException exception)
    {
        if (errorMappings.TryGetValue(
                new A2AHttpClientErrorMappingKey(statusCode, errorId),
                out var mapping))
        {
            return mapping.TryCreateException(
                message,
                details,
                out exception);
        }

        exception = null!;
        return false;
    }
}

internal interface IA2AClientErrorMapping
{
    string ErrorId { get; }

    object Error { get; }
}

internal interface IA2AJsonRpcClientErrorMapping : IA2AClientErrorMapping
{
    bool TryCreateException(
        JsonRpcError remoteError,
        out A2AOperationException exception);
}

internal sealed class A2AJsonRpcClientErrorMapping<TDetails>(
    A2AOperationError<TDetails> error,
    int code,
    JsonTypeInfo<TDetails> detailsTypeInfo)
    : IA2AJsonRpcClientErrorMapping
{
    public string ErrorId => error.ErrorId;

    public object Error => error;

    internal int Code { get; } = code;

    public bool TryCreateException(
        JsonRpcError remoteError,
        out A2AOperationException exception)
    {
        if (remoteError.Data is not { } data)
        {
            exception = null!;
            return false;
        }

        try
        {
            var details = data.Deserialize(detailsTypeInfo);
            if (details is null)
            {
                exception = null!;
                return false;
            }

            exception = new A2AOperationException<TDetails>(
                error,
                remoteError.Message,
                details);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            exception = null!;
            return false;
        }
    }
}

internal readonly record struct A2AHttpClientErrorMappingKey(
    int StatusCode,
    string ErrorId);

internal interface IA2AHttpClientErrorMapping : IA2AClientErrorMapping
{
    bool TryCreateException(
        string message,
        JsonElement details,
        out A2AOperationException exception);
}

internal sealed class A2AHttpClientErrorMapping<TDetails>(
    A2AOperationError<TDetails> error,
    int statusCode,
    JsonTypeInfo<TDetails> detailsTypeInfo)
    : IA2AHttpClientErrorMapping
{
    public string ErrorId => error.ErrorId;

    public object Error => error;

    internal int StatusCode { get; } = statusCode;

    public bool TryCreateException(
        string message,
        JsonElement details,
        out A2AOperationException exception)
    {
        try
        {
            TDetails? typedDetails;
            try
            {
                typedDetails = details.Deserialize(detailsTypeInfo);
            }
            catch (JsonException)
                when (details.ValueKind == JsonValueKind.Object
                    && details.TryGetProperty("value", out var value))
            {
                typedDetails = value.Deserialize(detailsTypeInfo);
            }

            if (typedDetails is null)
            {
                exception = null!;
                return false;
            }

            exception = new A2AOperationException<TDetails>(
                error,
                message,
                typedDetails);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            exception = null!;
            return false;
        }
    }
}

file static class A2AClientOperationBindingValidation
{
    internal static void ValidateErrorMappings(
        A2AOperationRegistration? operationRegistration,
        IEnumerable<IA2AClientErrorMapping> mappings)
    {
        if (operationRegistration is null)
        {
            return;
        }

        foreach (var mapping in mappings)
        {
            if (!operationRegistration.DeclaredErrors.TryGetValue(
                    mapping.ErrorId,
                    out var declaredError)
                || !ReferenceEquals(declaredError, mapping.Error))
            {
                throw new InvalidOperationException(
                    $"The A2A operation '{operationRegistration.Id.Value}' does not declare error '{mapping.ErrorId}' with the mapped details type.");
            }
        }
    }
}

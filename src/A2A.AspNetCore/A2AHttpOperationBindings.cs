using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A.AspNetCore;

/// <summary>Binds an HTTP request to a typed A2A operation request.</summary>
/// <typeparam name="TRequest">The operation request type.</typeparam>
/// <param name="httpContext">The current HTTP context.</param>
/// <param name="cancellationToken">A cancellation token.</param>
/// <returns>The typed operation request.</returns>
public delegate ValueTask<TRequest> A2AHttpRequestBinder<TRequest>(
    HttpContext httpContext,
    CancellationToken cancellationToken);

/// <summary>Builds HTTP+JSON bindings for typed unary and streaming operations.</summary>
public sealed class A2AHttpOperationBindingBuilder
{
    private readonly Dictionary<
        A2AHttpOperationBindingKey,
        IA2AHttpOperationBindingRegistration> _bindings = [];
    private readonly Dictionary<
        object,
        List<IA2AHttpOperationBindingRegistration>> _bindingsByOperation =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<
        object,
        Dictionary<string, IA2AHttpErrorMapping>> _errorMappingsByOperation =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Maps an HTTP route to a typed unary operation.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="httpMethod">The HTTP method.</param>
    /// <param name="route">The route pattern.</param>
    /// <param name="operation">The operation definition.</param>
    /// <param name="requestBinder">The request binder.</param>
    /// <param name="resultTypeInfo">The result serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AHttpOperationBindingBuilder Map<TRequest, TResult>(
        string httpMethod,
        [StringSyntax("Route")] string route,
        A2AOperation<TRequest, TResult> operation,
        A2AHttpRequestBinder<TRequest> requestBinder,
        JsonTypeInfo<TResult> resultTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requestBinder);
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        AddBinding(
            httpMethod,
            route,
            operation,
            new A2AHttpUnaryOperationBindingRegistration<TRequest, TResult>(
                operation,
                requestBinder,
                resultTypeInfo));
        return this;
    }

    /// <summary>Maps an HTTP route to a typed streaming operation.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="httpMethod">The HTTP method.</param>
    /// <param name="route">The route pattern.</param>
    /// <param name="operation">The streaming operation definition.</param>
    /// <param name="requestBinder">The request binder.</param>
    /// <param name="eventTypeInfo">The event serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AHttpOperationBindingBuilder MapStreaming<TRequest, TEvent>(
        string httpMethod,
        [StringSyntax("Route")] string route,
        A2AStreamingOperation<TRequest, TEvent> operation,
        A2AHttpRequestBinder<TRequest> requestBinder,
        JsonTypeInfo<TEvent> eventTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requestBinder);
        ArgumentNullException.ThrowIfNull(eventTypeInfo);

        AddBinding(
            httpMethod,
            route,
            operation,
            new A2AHttpStreamingOperationBindingRegistration<TRequest, TEvent>(
                operation,
                requestBinder,
                eventTypeInfo));
        return this;
    }

    /// <summary>Maps a declared unary operation error to an HTTP status.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <typeparam name="TDetails">The error details type.</typeparam>
    /// <param name="operation">The declaring unary operation.</param>
    /// <param name="error">The declared operation error.</param>
    /// <param name="statusCode">The HTTP error status.</param>
    /// <param name="detailsTypeInfo">Error details serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AHttpOperationBindingBuilder MapError<
        TRequest,
        TResult,
        TDetails>(
        A2AOperation<TRequest, TResult> operation,
        A2AOperationError<TDetails> error,
        int statusCode,
        JsonTypeInfo<TDetails> detailsTypeInfo)
        => MapErrorCore(operation, error, statusCode, detailsTypeInfo);

    /// <summary>Maps a declared streaming operation error to an HTTP status.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <typeparam name="TDetails">The error details type.</typeparam>
    /// <param name="operation">The declaring streaming operation.</param>
    /// <param name="error">The declared operation error.</param>
    /// <param name="statusCode">The HTTP error status.</param>
    /// <param name="detailsTypeInfo">Error details serialization metadata.</param>
    /// <returns>This builder.</returns>
    public A2AHttpOperationBindingBuilder MapError<
        TRequest,
        TEvent,
        TDetails>(
        A2AStreamingOperation<TRequest, TEvent> operation,
        A2AOperationError<TDetails> error,
        int statusCode,
        JsonTypeInfo<TDetails> detailsTypeInfo)
        => MapErrorCore(operation, error, statusCode, detailsTypeInfo);

    /// <summary>Builds the immutable HTTP operation bindings.</summary>
    /// <returns>The operation bindings.</returns>
    public A2AHttpOperationBindings Build()
        => new(
            _bindings.Values
                .Select(static registration => registration.Freeze())
                .ToArray());

    private A2AHttpOperationBindingBuilder MapErrorCore<TDetails>(
        object operation,
        A2AOperationError<TDetails> error,
        int statusCode,
        JsonTypeInfo<TDetails> detailsTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(detailsTypeInfo);
        if (statusCode is < StatusCodes.Status400BadRequest or > 599)
        {
            throw new ArgumentOutOfRangeException(
                nameof(statusCode),
                statusCode,
                "An HTTP operation error status must be between 400 and 599.");
        }

        if (!_bindingsByOperation.TryGetValue(operation, out var registrations))
        {
            throw new InvalidOperationException(
                "The A2A operation must have an HTTP binding before its errors can be mapped.");
        }

        if (!_errorMappingsByOperation.TryGetValue(
                operation,
                out var operationMappings))
        {
            operationMappings = new Dictionary<string, IA2AHttpErrorMapping>(
                StringComparer.Ordinal);
            _errorMappingsByOperation.Add(operation, operationMappings);
        }

        var mapping = new A2AHttpErrorMapping<TDetails>(
            error,
            statusCode,
            detailsTypeInfo);
        if (!operationMappings.TryAdd(mapping.ErrorId, mapping))
        {
            throw new InvalidOperationException(
                $"The A2A operation error '{mapping.ErrorId}' already has an HTTP mapping.");
        }

        foreach (var registration in registrations)
        {
            registration.AddErrorMapping(mapping);
        }

        return this;
    }

    private void AddBinding(
        string httpMethod,
        [StringSyntax("Route")] string route,
        object operation,
        IA2AHttpOperationBindingRegistration registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(httpMethod);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        RoutePatternFactory.Parse(route);

        var key = new A2AHttpOperationBindingKey(httpMethod, route);
        if (!_bindings.TryAdd(key, registration))
        {
            throw new InvalidOperationException(
                $"An HTTP operation binding is already registered for '{httpMethod} {route}'.");
        }

        registration.SetRoute(key.HttpMethod, route);
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

/// <summary>Contains HTTP+JSON bindings for typed unary and streaming operations.</summary>
public sealed class A2AHttpOperationBindings
{
    private readonly IReadOnlyList<IA2AHttpOperationBindingRegistration>
        _registrations;

    internal A2AHttpOperationBindings(
        IReadOnlyList<IA2AHttpOperationBindingRegistration> registrations)
    {
        _registrations = registrations.ToArray();
    }

    internal void MapEndpoints(
        RouteGroupBuilder routeGroup,
        A2ARequestScopeFactory scopeFactory,
        A2AOperationHandlerCatalog handlers,
        IReadOnlySet<A2AHttpOperationBindingKey>? routeKeys = null)
    {
        var logger = ((IEndpointRouteBuilder)routeGroup).ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("A2A.REST");
        foreach (var registration in _registrations)
        {
            if (routeKeys is not null
                && !routeKeys.Contains(registration.RouteKey))
            {
                continue;
            }

            registration.Build(handlers.OperationCatalog).MapEndpoint(
                routeGroup,
                scopeFactory,
                handlers,
                logger);
        }
    }

    internal IReadOnlySet<A2AHttpOperationBindingKey>
        GetMissingCanonicalStandardRouteKeys(
        A2AOperationCatalog operationCatalog)
    {
        var missingRouteKeys = new HashSet<A2AHttpOperationBindingKey>(
            A2AStandardHttpBindingBuilderExtensions
                .CanonicalRouteOperationIds.Keys);
        foreach (var registration in _registrations)
        {
            _ = registration.Build(operationCatalog);
            if (A2AStandardHttpBindingBuilderExtensions
                    .CanonicalRouteOperationIds.TryGetValue(
                        registration.RouteKey,
                        out var expectedOperationId)
                && registration.TryGetStandardOperationId(
                    operationCatalog,
                    out var operationId)
                && operationId == expectedOperationId)
            {
                missingRouteKeys.Remove(registration.RouteKey);
            }
        }

        return missingRouteKeys;
    }
}

internal readonly record struct A2AHttpOperationBindingKey
{
    internal A2AHttpOperationBindingKey(string httpMethod, string route)
    {
        HttpMethod = httpMethod.ToUpperInvariant();
        Route = A2AStandardHttpBindingBuilderExtensions
            .NormalizeRoutePattern(route);
    }

    internal string HttpMethod { get; }

    internal string Route { get; }
}

internal interface IA2AHttpOperationBindingRegistration
{
    A2AHttpOperationBindingKey RouteKey { get; }

    void SetRoute(string httpMethod, string route);

    void AddErrorMapping(IA2AHttpErrorMapping mapping);

    IA2AHttpOperationBindingRegistration Freeze();

    bool TryGetStandardOperationId(
        A2AOperationCatalog operationCatalog,
        out A2AOperationId operationId);

    IA2AHttpOperationBinding Build(A2AOperationCatalog operationCatalog);
}

internal abstract class A2AHttpOperationBindingRegistration
    : IA2AHttpOperationBindingRegistration
{
    private readonly Dictionary<string, IA2AHttpErrorMapping> _errorMappings =
        new(StringComparer.Ordinal);
    private string? _httpMethod;
    private string? _route;

    protected abstract object Operation { get; }

    protected abstract A2AOperationKind Kind { get; }

    protected abstract Type RequestType { get; }

    protected abstract Type ResponseType { get; }

    public A2AHttpOperationBindingKey RouteKey =>
        new(
            _httpMethod
                ?? throw new InvalidOperationException(
                    "The HTTP binding has no method."),
            _route
                ?? throw new InvalidOperationException(
                    "The HTTP binding has no route."));

    public void SetRoute(string httpMethod, string route)
    {
        _httpMethod = httpMethod;
        _route = route;
    }

    public void AddErrorMapping(IA2AHttpErrorMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (!_errorMappings.TryAdd(mapping.ErrorId, mapping))
        {
            throw new InvalidOperationException(
                $"The A2A operation error '{mapping.ErrorId}' already has an HTTP mapping.");
        }
    }

    public IA2AHttpOperationBindingRegistration Freeze()
    {
        var clone = CloneCore();
        clone.SetRoute(
            _httpMethod
                ?? throw new InvalidOperationException(
                    "The HTTP binding has no method."),
            _route
                ?? throw new InvalidOperationException(
                    "The HTTP binding has no route."));
        foreach (var mapping in _errorMappings.Values)
        {
            clone.AddErrorMapping(mapping);
        }

        return clone;
    }

    public bool TryGetStandardOperationId(
        A2AOperationCatalog operationCatalog,
        out A2AOperationId operationId)
    {
        if (Operation is IA2AOperationHandle operationHandle
            && operationCatalog.TryGetRegistration(
                operationHandle.Id,
                out var registration)
            && ReferenceEquals(registration.Handle, Operation)
            && registration.Source == A2AOperationSource.Standard)
        {
            operationId = registration.Id;
            return true;
        }

        operationId = default;
        return false;
    }

    public IA2AHttpOperationBinding Build(A2AOperationCatalog operationCatalog)
    {
        if (Operation is not IA2AOperationHandle operationHandle
            || !operationCatalog.TryGetRegistration(
                operationHandle.Id,
                out var operationRegistration)
            || !ReferenceEquals(operationRegistration.Handle, Operation))
        {
            throw new InvalidOperationException(
                "The HTTP binding operation is not defined by the supplied operation catalog.");
        }

        if (operationRegistration.Kind != Kind
            || operationRegistration.RequestType != RequestType
            || operationRegistration.ResponseType != ResponseType)
        {
            throw new InvalidOperationException(
                $"The HTTP binding for '{operationHandle.Id.Value}' uses incompatible operation types.");
        }

        var httpMethod = _httpMethod
            ?? throw new InvalidOperationException("The HTTP binding has no method.");
        var route = _route
            ?? throw new InvalidOperationException("The HTTP binding has no route.");
        A2AStandardHttpBindingBuilderExtensions.ValidateReservedRouteBinding(
            httpMethod,
            route,
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
            httpMethod,
            route,
            operationCatalog,
            operationRegistration,
            new Dictionary<string, IA2AHttpErrorMapping>(
                _errorMappings,
                StringComparer.Ordinal));
    }

    protected abstract IA2AHttpOperationBinding BuildCore(
        string httpMethod,
        string route,
        A2AOperationCatalog operationCatalog,
        A2AOperationRegistration operationRegistration,
        IReadOnlyDictionary<string, IA2AHttpErrorMapping> errorMappings);

    protected abstract A2AHttpOperationBindingRegistration CloneCore();
}

internal sealed class A2AHttpUnaryOperationBindingRegistration<TRequest, TResult>(
    A2AOperation<TRequest, TResult> operation,
    A2AHttpRequestBinder<TRequest> requestBinder,
    JsonTypeInfo<TResult> resultTypeInfo)
    : A2AHttpOperationBindingRegistration
{
    protected override object Operation => operation;

    protected override A2AOperationKind Kind => A2AOperationKind.Unary;

    protected override Type RequestType => typeof(TRequest);

    protected override Type ResponseType => typeof(TResult);

    protected override IA2AHttpOperationBinding BuildCore(
        string httpMethod,
        string route,
        A2AOperationCatalog operationCatalog,
        A2AOperationRegistration operationRegistration,
        IReadOnlyDictionary<string, IA2AHttpErrorMapping> errorMappings)
        => new A2AHttpUnaryOperationBinding<TRequest, TResult>(
            httpMethod,
            route,
            operationCatalog,
            operationRegistration.Source,
            operation,
            requestBinder,
            resultTypeInfo,
            errorMappings);

    protected override A2AHttpOperationBindingRegistration CloneCore() =>
        new A2AHttpUnaryOperationBindingRegistration<TRequest, TResult>(
            operation,
            requestBinder,
            resultTypeInfo);
}

internal sealed class A2AHttpStreamingOperationBindingRegistration<TRequest, TEvent>(
    A2AStreamingOperation<TRequest, TEvent> operation,
    A2AHttpRequestBinder<TRequest> requestBinder,
    JsonTypeInfo<TEvent> eventTypeInfo)
    : A2AHttpOperationBindingRegistration
{
    protected override object Operation => operation;

    protected override A2AOperationKind Kind => A2AOperationKind.Streaming;

    protected override Type RequestType => typeof(TRequest);

    protected override Type ResponseType => typeof(TEvent);

    protected override IA2AHttpOperationBinding BuildCore(
        string httpMethod,
        string route,
        A2AOperationCatalog operationCatalog,
        A2AOperationRegistration operationRegistration,
        IReadOnlyDictionary<string, IA2AHttpErrorMapping> errorMappings)
        => new A2AHttpStreamingOperationBinding<TRequest, TEvent>(
            httpMethod,
            route,
            operationCatalog,
            operationRegistration.Source,
            operation,
            requestBinder,
            eventTypeInfo,
            errorMappings);

    protected override A2AHttpOperationBindingRegistration CloneCore() =>
        new A2AHttpStreamingOperationBindingRegistration<TRequest, TEvent>(
            operation,
            requestBinder,
            eventTypeInfo);
}

internal interface IA2AHttpOperationBinding
{
    void MapEndpoint(
        RouteGroupBuilder routeGroup,
        A2ARequestScopeFactory scopeFactory,
        A2AOperationHandlerCatalog handlers,
        ILogger logger);
}

internal interface IA2AHttpBoundOperation
{
    ValueTask<IResult> InvokeAsync(
        A2AOperationContext context,
        A2AOperationHandlerCatalog handlers,
        Activity? operationActivity,
        CancellationToken cancellationToken);
}

internal abstract class A2AHttpOperationBinding(
    string httpMethod,
    string route,
    IReadOnlyDictionary<string, IA2AHttpErrorMapping> errorMappings)
    : IA2AHttpOperationBinding
{
    public void MapEndpoint(
        RouteGroupBuilder routeGroup,
        A2ARequestScopeFactory scopeFactory,
        A2AOperationHandlerCatalog handlers,
        ILogger logger)
    {
        routeGroup.MapMethods(
            route,
            [httpMethod],
            (HttpContext httpContext, CancellationToken cancellationToken) =>
                InvokeAsync(
                    httpContext,
                    scopeFactory,
                    handlers,
                    logger,
                    cancellationToken));
    }

    protected abstract ValueTask<IA2AHttpBoundOperation> BindAsync(
        HttpContext httpContext,
        ILogger logger,
        CancellationToken cancellationToken);

    protected abstract A2AOperationDiagnosticContext Diagnostics { get; }

    protected IResult CreateErrorResult(
        Exception exception,
        ILogger logger)
    {
        if (exception is A2AException a2aException)
        {
            logger.A2AErrorInActivityName(
                exception,
                "HandleA2AHttpRequest");
            return new A2AErrorResult(a2aException);
        }

        if (exception is A2AOperationException operationException
            && errorMappings.TryGetValue(
                operationException.ErrorId,
                out var mapping)
            && mapping.TryCreateResult(
                operationException,
                out var mappedResult))
        {
            return mappedResult;
        }

        logger.UnexpectedErrorInActivityName(
            exception,
            "HandleA2AHttpRequest");
        return CreateInternalErrorResult();
    }

    protected void LogStreamException(
        Exception exception,
        ILogger logger)
    {
        if (exception is A2AException
            || exception is A2AOperationException operationException
                && errorMappings.ContainsKey(operationException.ErrorId))
        {
            logger.A2AErrorInActivityName(
                exception,
                "HandleA2AHttpRequest");
            return;
        }

        logger.UnexpectedErrorInActivityName(
            exception,
            "HandleA2AHttpRequest");
    }

    private async Task<IResult> InvokeAsync(
        HttpContext httpContext,
        A2ARequestScopeFactory scopeFactory,
        A2AOperationHandlerCatalog handlers,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var activity = A2AAspNetCoreDiagnostics.Source.StartActivity(
            "HandleA2AHttpRequest",
            ActivityKind.Server);
        activity?.SetTag("http.request.method", httpMethod);
        activity?.SetTag("http.route", route);

        A2ARequestScope? scope = null;
        Activity? operationActivity = Diagnostics.Start();
        try
        {
            var boundOperation = await BindAsync(
                httpContext,
                logger,
                cancellationToken).ConfigureAwait(false);

            scope = await scopeFactory(
                httpContext,
                cancellationToken).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(scope);

            var result = await boundOperation.InvokeAsync(
                scope.Context,
                handlers,
                operationActivity,
                cancellationToken).ConfigureAwait(false);
            return WrapScope(result, ref scope, ref operationActivity);
        }
        catch (A2AHttpBindingResultException exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            A2AOperationDiagnostics.SetError(
                operationActivity,
                exception.BindingException,
                cancellationToken);
            return WrapScope(exception.Result, ref scope, ref operationActivity);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            A2AOperationDiagnostics.SetError(operationActivity, exception, cancellationToken);
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            A2AOperationDiagnostics.SetError(operationActivity, exception, cancellationToken);
            return WrapScope(
                CreateErrorResult(exception, logger),
                ref scope, ref operationActivity);
        }
        finally
        {
            try
            {
                if (scope is not null)
                {
                    await A2AOperationDiagnostics.DisposeAsync(scope, operationActivity, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                operationActivity?.Dispose();
            }
        }
    }

    private static A2AErrorResult CreateInternalErrorResult() =>
        new A2AErrorResult(
            new A2AException(
                "An internal error occurred.",
                A2AErrorCode.InternalError));

    private static IResult WrapScope(
        IResult result,
        ref A2ARequestScope? scope,
        ref Activity? operationActivity)
    {
        if (scope is null && operationActivity is null)
        {
            return result;
        }

        var scopedResult = new A2ARequestScopeResult(result, scope, operationActivity);
        scope = null;
        operationActivity = null;
        return scopedResult;
    }
}

internal sealed class A2AHttpUnaryOperationBinding<TRequest, TResult>(
    string httpMethod,
    string route,
    A2AOperationCatalog operationCatalog,
    A2AOperationSource operationSource,
    A2AOperation<TRequest, TResult> operation,
    A2AHttpRequestBinder<TRequest> requestBinder,
    JsonTypeInfo<TResult> resultTypeInfo,
    IReadOnlyDictionary<string, IA2AHttpErrorMapping> errorMappings)
    : A2AHttpOperationBinding(httpMethod, route, errorMappings)
{
    protected override A2AOperationDiagnosticContext Diagnostics =>
        new(operation.Id, A2AOperationKind.Unary, operationSource, "http-json");

    protected override async ValueTask<IA2AHttpBoundOperation> BindAsync(
        HttpContext httpContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var request = await BindRequestAsync(
            requestBinder,
            httpContext,
            cancellationToken).ConfigureAwait(false);
        operationCatalog.Validate(operation, request);
        return new BoundOperation(
            operation,
            request,
            resultTypeInfo);
    }

    private sealed class BoundOperation(
        A2AOperation<TRequest, TResult> operation,
        TRequest request,
        JsonTypeInfo<TResult> resultTypeInfo)
        : IA2AHttpBoundOperation
    {
        public async ValueTask<IResult> InvokeAsync(
            A2AOperationContext context,
            A2AOperationHandlerCatalog handlers,
            Activity? operationActivity,
            CancellationToken cancellationToken)
        {
            var result = await handlers.InvokeAsync(
                operation,
                context,
                request,
                cancellationToken).ConfigureAwait(false);
            return result is A2AEmptyResult
                ? Results.NoContent()
                : new A2AHttpOperationResult<TResult>(
                    result,
                    resultTypeInfo,
                    operationActivity);
        }
    }

    private static async ValueTask<TRequest> BindRequestAsync(
        A2AHttpRequestBinder<TRequest> binder,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await binder(
                httpContext,
                cancellationToken).ConfigureAwait(false);
            if (request is null)
            {
                throw new A2AException(
                    $"Failed to bind the HTTP request as {typeof(TRequest).Name}.",
                    A2AErrorCode.InvalidParams);
            }

            return request;
        }
        catch (A2AHttpBindingException exception)
        {
            throw new A2AHttpBindingResultException(exception);
        }
        catch (A2AException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new A2AException(
                $"Invalid HTTP request body for {typeof(TRequest).Name}.",
                exception,
                A2AErrorCode.InvalidParams);
        }
        catch (FormatException exception)
        {
            throw new A2AException(
                $"Invalid HTTP request value for {typeof(TRequest).Name}.",
                exception,
                A2AErrorCode.InvalidParams);
        }
        catch (OverflowException exception)
        {
            throw new A2AException(
                $"Invalid HTTP request value for {typeof(TRequest).Name}.",
                exception,
                A2AErrorCode.InvalidParams);
        }
    }
}

internal sealed class A2AHttpStreamingOperationBinding<TRequest, TEvent>(
    string httpMethod,
    string route,
    A2AOperationCatalog operationCatalog,
    A2AOperationSource operationSource,
    A2AStreamingOperation<TRequest, TEvent> operation,
    A2AHttpRequestBinder<TRequest> requestBinder,
    JsonTypeInfo<TEvent> eventTypeInfo,
    IReadOnlyDictionary<string, IA2AHttpErrorMapping> errorMappings)
    : A2AHttpOperationBinding(httpMethod, route, errorMappings)
{
    protected override A2AOperationDiagnosticContext Diagnostics =>
        new(operation.Id, A2AOperationKind.Streaming, operationSource, "http-json");

    protected override async ValueTask<IA2AHttpBoundOperation> BindAsync(
        HttpContext httpContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var request = await BindRequestAsync(
            requestBinder,
            httpContext,
            cancellationToken).ConfigureAwait(false);
        operationCatalog.ValidateStreaming(operation, request);
        return new BoundOperation(
            operation,
            request,
            eventTypeInfo,
            exception => CreateErrorResult(exception, logger),
            exception => LogStreamException(exception, logger));
    }

    private sealed class BoundOperation(
        A2AStreamingOperation<TRequest, TEvent> operation,
        TRequest request,
        JsonTypeInfo<TEvent> eventTypeInfo,
        Func<Exception, IResult> createErrorResult,
        Action<Exception> logStreamException)
        : IA2AHttpBoundOperation
    {
        public ValueTask<IResult> InvokeAsync(
            A2AOperationContext context,
            A2AOperationHandlerCatalog handlers,
            Activity? operationActivity,
            CancellationToken cancellationToken)
        {
            var events = handlers.InvokeStreamingAsync(
                    operation,
                    context,
                    request,
                    cancellationToken);

            return ValueTask.FromResult<IResult>(
                new A2AEventStreamResult<TEvent>(
                    events,
                    eventTypeInfo,
                    createErrorResult,
                    operationActivity,
                    logStreamException));
        }
    }

    private static async ValueTask<TRequest> BindRequestAsync(
        A2AHttpRequestBinder<TRequest> binder,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await binder(
                httpContext,
                cancellationToken).ConfigureAwait(false);
            if (request is null)
            {
                throw new A2AException(
                    $"Failed to bind the HTTP request as {typeof(TRequest).Name}.",
                    A2AErrorCode.InvalidParams);
            }

            return request;
        }
        catch (A2AHttpBindingException exception)
        {
            throw new A2AHttpBindingResultException(exception);
        }
        catch (A2AException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new A2AException(
                $"Invalid HTTP request body for {typeof(TRequest).Name}.",
                exception,
                A2AErrorCode.InvalidParams);
        }
        catch (FormatException exception)
        {
            throw new A2AException(
                $"Invalid HTTP request value for {typeof(TRequest).Name}.",
                exception,
                A2AErrorCode.InvalidParams);
        }
        catch (OverflowException exception)
        {
            throw new A2AException(
                $"Invalid HTTP request value for {typeof(TRequest).Name}.",
                exception,
                A2AErrorCode.InvalidParams);
        }
    }
}

internal interface IA2AHttpErrorMapping
{
    string ErrorId { get; }

    object Error { get; }

    bool TryCreateResult(
        A2AOperationException exception,
        out IResult result);
}

internal sealed class A2AHttpErrorMapping<TDetails>(
    A2AOperationError<TDetails> error,
    int statusCode,
    JsonTypeInfo<TDetails> detailsTypeInfo)
    : IA2AHttpErrorMapping
{
    public string ErrorId => error.ErrorId;

    public object Error => error;

    public bool TryCreateResult(
        A2AOperationException exception,
        out IResult result)
    {
        if (exception is not A2AOperationException<TDetails> typedException
            || !ReferenceEquals(typedException.Error, error))
        {
            result = null!;
            return false;
        }

        try
        {
            var details = JsonSerializer.SerializeToElement(
                typedException.Details,
                detailsTypeInfo);
            result = new A2AHttpOperationErrorResult(
                statusCode,
                typedException.Message,
                error.ErrorId,
                details);
            return true;
        }
        catch (Exception)
        {
            result = null!;
            return false;
        }
    }
}

/// <summary>
/// Represents an HTTP request binding failure that returns an exact HTTP result.
/// </summary>
/// <remarks>
/// Throw this exception from an <see cref="A2AHttpRequestBinder{TRequest}"/> to
/// reject transport input before an A2A request scope is created. Exceptions
/// thrown after the request binder completes are handled as operation failures.
/// </remarks>
public sealed class A2AHttpBindingException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="A2AHttpBindingException"/>
    /// class.
    /// </summary>
    /// <param name="result">The exact HTTP result to return.</param>
    public A2AHttpBindingException(IResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Result = result;
    }

    internal IResult Result { get; }
}

internal sealed class A2AHttpBindingResultException(
    A2AHttpBindingException bindingException)
    : Exception(bindingException.Message, bindingException)
{
    internal A2AHttpBindingException BindingException { get; } =
        bindingException;

    internal IResult Result => BindingException.Result;
}

internal sealed class A2AHttpOperationResult<TResult>(
    TResult result,
    JsonTypeInfo<TResult> resultTypeInfo,
    Activity? operationActivity)
    : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        using var buffer = new MemoryStream();
        try
        {
            await JsonSerializer.SerializeAsync(
                buffer,
                result,
                resultTypeInfo,
                httpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (httpContext.RequestAborted.IsCancellationRequested)
        {
            A2AOperationDiagnostics.SetError(operationActivity, exception, httpContext.RequestAborted);
            return;
        }
        catch (Exception exception)
        {
            A2AOperationDiagnostics.SetError(operationActivity, exception, httpContext.RequestAborted);
            await new A2AErrorResult(
                new A2AException(
                    "An internal error occurred.",
                    A2AErrorCode.InternalError))
                .ExecuteAsync(httpContext).ConfigureAwait(false);
            return;
        }

        buffer.Position = 0;
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "application/json";
        await buffer.CopyToAsync(
            httpContext.Response.Body,
            httpContext.RequestAborted).ConfigureAwait(false);
    }
}

internal sealed class A2AHttpOperationErrorResult(
    int statusCode,
    string message,
    string errorId,
    JsonElement details)
    : IResult,
      IStatusCodeHttpResult
{
    public int? StatusCode => statusCode;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteNumber("code", statusCode);
            writer.WriteString("status", GetStatusName(statusCode));
            writer.WriteString("message", message);
            writer.WritePropertyName("details");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("@type", errorId);
            if (details.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in details.EnumerateObject())
                {
                    if (!property.NameEquals("@type"))
                    {
                        property.WriteTo(writer);
                    }
                }
            }
            else
            {
                writer.WritePropertyName("value");
                details.WriteTo(writer);
            }

            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(
            httpContext.Response.Body,
            httpContext.RequestAborted).ConfigureAwait(false);
    }

    private static string GetStatusName(int value) =>
        value switch
        {
            StatusCodes.Status400BadRequest => "INVALID_ARGUMENT",
            StatusCodes.Status401Unauthorized => "UNAUTHENTICATED",
            StatusCodes.Status403Forbidden => "PERMISSION_DENIED",
            StatusCodes.Status404NotFound => "NOT_FOUND",
            StatusCodes.Status409Conflict => "ABORTED",
            StatusCodes.Status429TooManyRequests => "RESOURCE_EXHAUSTED",
            StatusCodes.Status499ClientClosedRequest => "CANCELLED",
            StatusCodes.Status500InternalServerError => "INTERNAL",
            StatusCodes.Status501NotImplemented => "UNIMPLEMENTED",
            StatusCodes.Status503ServiceUnavailable => "UNAVAILABLE",
            StatusCodes.Status504GatewayTimeout => "DEADLINE_EXCEEDED",
            _ => "UNKNOWN",
        };
}

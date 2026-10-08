using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Patterns;
using System.Globalization;
using System.Text;

namespace A2A.AspNetCore;

/// <summary>
/// Binds an HTTP request to a typed custom operation request.
/// </summary>
/// <typeparam name="TRequest">The custom operation request type.</typeparam>
/// <param name="context">The current HTTP context.</param>
/// <param name="cancellationToken">The request cancellation token.</param>
/// <returns>The typed custom operation request.</returns>
public delegate ValueTask<TRequest> A2AHttpRequestBinder<TRequest>(
    HttpContext context,
    CancellationToken cancellationToken);

/// <summary>
/// Builds HTTP route mappings for custom A2A operations.
/// </summary>
public sealed class A2AHttpCustomOperationBuilder
{
    private static readonly HashSet<HttpRouteKey> s_standardRoutes =
    [
        new(HttpMethods.Get, "/tasks/{id}"),
        new(HttpMethods.Post, "/tasks/{id}:cancel"),
        new(HttpMethods.Post, "/tasks/{id}:subscribe"),
        new(HttpMethods.Get, "/tasks"),
        new(HttpMethods.Post, "/message:send"),
        new(HttpMethods.Post, "/message:stream"),
        new(HttpMethods.Post, "/tasks/{id}/pushNotificationConfigs"),
        new(HttpMethods.Get, "/tasks/{id}/pushNotificationConfigs"),
        new(HttpMethods.Get, "/tasks/{id}/pushNotificationConfigs/{configId}"),
        new(HttpMethods.Delete, "/tasks/{id}/pushNotificationConfigs/{configId}"),
        new(HttpMethods.Get, "/extendedAgentCard"),
    ];

    private readonly Dictionary<HttpRouteKey, HttpCustomOperationBinding> _bindings = [];
    private bool _built;

    /// <summary>
    /// Maps an HTTP method and route to a unary custom operation.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="httpMethod">The HTTP method.</param>
    /// <param name="route">The route pattern relative to the A2A HTTP route group.</param>
    /// <param name="operation">The custom operation handle.</param>
    /// <param name="binder">The HTTP request binder.</param>
    /// <returns>This builder.</returns>
    public A2AHttpCustomOperationBuilder Map<TRequest, TResult>(
        string httpMethod,
        string route,
        A2ACustomOperation<TRequest, TResult> operation,
        A2AHttpRequestBinder<TRequest> binder)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(binder);
        Add(
            httpMethod,
            route,
            operation.RegistryToken,
            operation.Registration,
            async (context, cancellationToken) =>
                await binder(context, cancellationToken).ConfigureAwait(false));
        return this;
    }

    /// <summary>
    /// Maps an HTTP method and route to a server-streaming custom operation.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="httpMethod">The HTTP method.</param>
    /// <param name="route">The route pattern relative to the A2A HTTP route group.</param>
    /// <param name="operation">The custom operation handle.</param>
    /// <param name="binder">The HTTP request binder.</param>
    /// <returns>This builder.</returns>
    public A2AHttpCustomOperationBuilder MapStreaming<TRequest, TEvent>(
        string httpMethod,
        string route,
        A2AStreamingCustomOperation<TRequest, TEvent> operation,
        A2AHttpRequestBinder<TRequest> binder)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(binder);
        Add(
            httpMethod,
            route,
            operation.RegistryToken,
            operation.Registration,
            async (context, cancellationToken) =>
                await binder(context, cancellationToken).ConfigureAwait(false));
        return this;
    }

    /// <summary>
    /// Finalizes the HTTP custom operation mappings.
    /// </summary>
    /// <param name="registry">The registry that owns every mapped operation.</param>
    /// <returns>The immutable HTTP custom operation bindings.</returns>
    public A2AHttpCustomOperationBindings Build(A2ACustomOperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (_built)
        {
            throw new InvalidOperationException("The HTTP custom operation bindings have already been built.");
        }

        foreach (var binding in _bindings.Values)
        {
            registry.EnsureOwnership(binding.RegistryToken);
        }

        _built = true;
        return new A2AHttpCustomOperationBindings(_bindings.Values);
    }

    private void Add(
        string httpMethod,
        string route,
        object registryToken,
        CustomOperationRegistration registration,
        Func<HttpContext, CancellationToken, ValueTask<object?>> binder)
    {
        if (_built)
        {
            throw new InvalidOperationException("The HTTP custom operation bindings have already been built.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(httpMethod);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        var normalizedMethod = httpMethod.ToUpperInvariant();
        var key = new HttpRouteKey(normalizedMethod, route);
        if (s_standardRoutes.Contains(key))
        {
            throw new InvalidOperationException(
                $"Custom HTTP route '{normalizedMethod} {route}' cannot replace a standard A2A route.");
        }

        if (!_bindings.TryAdd(
            key,
            new HttpCustomOperationBinding(
                normalizedMethod,
                route,
                registryToken,
                registration,
                binder)))
        {
            throw new InvalidOperationException(
                $"Custom HTTP route '{normalizedMethod} {route}' is already mapped.");
        }
    }

    private static string CanonicalizeRoute(string route)
    {
        var pattern = RoutePatternFactory.Parse(route);
        var canonicalRoute = new StringBuilder();
        foreach (var segment in pattern.PathSegments)
        {
            canonicalRoute.Append('/');
            foreach (var part in segment.Parts)
            {
                switch (part)
                {
                    case RoutePatternLiteralPart literal:
                        canonicalRoute.Append(literal.Content.ToLowerInvariant());
                        break;
                    case RoutePatternSeparatorPart separator:
                        canonicalRoute.Append(separator.Content.ToLowerInvariant());
                        break;
                    case RoutePatternParameterPart parameter:
                        canonicalRoute.Append('{');
                        if (parameter.IsCatchAll)
                        {
                            canonicalRoute.Append(parameter.EncodeSlashes ? '*' : "**");
                        }

                        canonicalRoute.Append('_');
                        foreach (var policy in parameter.ParameterPolicies)
                        {
                            canonicalRoute.Append(':').Append(policy.Content);
                        }

                        if (parameter.Default is not null)
                        {
                            canonicalRoute
                                .Append('=')
                                .Append(Convert.ToString(parameter.Default, CultureInfo.InvariantCulture));
                        }

                        if (parameter.IsOptional)
                        {
                            canonicalRoute.Append('?');
                        }

                        canonicalRoute.Append('}');
                        break;
                }
            }
        }

        return canonicalRoute.Length == 0 ? "/" : canonicalRoute.ToString();
    }

    private readonly record struct HttpRouteKey
    {
        public HttpRouteKey(string method, string route)
        {
            Method = method;
            Route = CanonicalizeRoute(route);
        }

        public string Method { get; }

        public string Route { get; }
    }
}

/// <summary>
/// Immutable HTTP route mappings for custom A2A operations.
/// </summary>
public sealed class A2AHttpCustomOperationBindings
{
    private readonly HttpCustomOperationBinding[] _bindings;

    internal A2AHttpCustomOperationBindings(
        IEnumerable<HttpCustomOperationBinding> bindings)
    {
        _bindings = bindings.ToArray();
    }

    internal IReadOnlyList<HttpCustomOperationBinding> Items => _bindings;
}

internal sealed record HttpCustomOperationBinding(
    string HttpMethod,
    string Route,
    object RegistryToken,
    CustomOperationRegistration Registration,
    Func<HttpContext, CancellationToken, ValueTask<object?>> BindAsync);

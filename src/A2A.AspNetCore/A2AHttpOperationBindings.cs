using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using System.Diagnostics.CodeAnalysis;
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

/// <summary>Builds HTTP+JSON bindings for custom unary operations.</summary>
public sealed class A2AHttpOperationBindingBuilder
{
    private readonly List<IA2AHttpOperationBinding> _bindings = [];

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
        ArgumentException.ThrowIfNullOrEmpty(httpMethod);
        ArgumentException.ThrowIfNullOrEmpty(route);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requestBinder);
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        _bindings.Add(new A2AHttpOperationBinding<TRequest, TResult>(
            httpMethod,
            route,
            operation,
            requestBinder,
            resultTypeInfo));
        return this;
    }

    /// <summary>Builds the immutable HTTP operation bindings.</summary>
    /// <returns>The operation bindings.</returns>
    public A2AHttpOperationBindings Build()
        => new(_bindings);
}

/// <summary>Contains HTTP+JSON bindings for custom unary operations.</summary>
public sealed class A2AHttpOperationBindings
{
    private readonly IReadOnlyList<IA2AHttpOperationBinding> _bindings;

    internal A2AHttpOperationBindings(
        IReadOnlyList<IA2AHttpOperationBinding> bindings)
    {
        _bindings = bindings.ToArray();
    }

    internal void MapEndpoints(
        RouteGroupBuilder routeGroup,
        A2ARequestScopeFactory scopeFactory,
        A2AOperationHandlerCatalog handlers)
    {
        foreach (var binding in _bindings)
        {
            binding.MapEndpoint(routeGroup, scopeFactory, handlers);
        }
    }
}

internal interface IA2AHttpOperationBinding
{
    void MapEndpoint(
        RouteGroupBuilder routeGroup,
        A2ARequestScopeFactory scopeFactory,
        A2AOperationHandlerCatalog handlers);
}

internal sealed class A2AHttpOperationBinding<TRequest, TResult>(
    string httpMethod,
    string route,
    A2AOperation<TRequest, TResult> operation,
    A2AHttpRequestBinder<TRequest> requestBinder,
    JsonTypeInfo<TResult> resultTypeInfo)
    : IA2AHttpOperationBinding
{
    public void MapEndpoint(
        RouteGroupBuilder routeGroup,
        A2ARequestScopeFactory scopeFactory,
        A2AOperationHandlerCatalog handlers)
    {
        routeGroup.MapMethods(
            route,
            [httpMethod],
            (HttpContext httpContext, CancellationToken cancellationToken) =>
                InvokeAsync(
                    httpContext,
                    scopeFactory,
                    handlers,
                    cancellationToken));
    }

    private async Task<IResult> InvokeAsync(
        HttpContext httpContext,
        A2ARequestScopeFactory scopeFactory,
        A2AOperationHandlerCatalog handlers,
        CancellationToken cancellationToken)
    {
        A2ARequestScope? scope = null;
        try
        {
            scope = await scopeFactory(
                httpContext,
                cancellationToken).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(scope);

            var request = await requestBinder(
                httpContext,
                cancellationToken).ConfigureAwait(false);
            var result = await handlers.InvokeAsync(
                operation,
                scope.Context,
                request,
                cancellationToken).ConfigureAwait(false);

            return WrapScope(
                new A2AHttpOperationResult<TResult>(result, resultTypeInfo),
                ref scope);
        }
        catch (A2AException exception)
        {
            return WrapScope(
                new A2AErrorResult(exception),
                ref scope);
        }
        finally
        {
            if (scope is not null)
            {
                await scope.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static A2ARequestScopeResult WrapScope(
        IResult result,
        ref A2ARequestScope? scope)
    {
        var scopedResult = new A2ARequestScopeResult(result, scope!);
        scope = null;
        return scopedResult;
    }
}

internal sealed class A2AHttpOperationResult<TResult>(
    TResult result,
    JsonTypeInfo<TResult> resultTypeInfo)
    : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            result,
            resultTypeInfo,
            httpContext.RequestAborted);
    }
}

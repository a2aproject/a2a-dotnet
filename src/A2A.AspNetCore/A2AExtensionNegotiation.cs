using Microsoft.AspNetCore.Http;

namespace A2A.AspNetCore;

/// <summary>Shared extension negotiation for JSON-RPC and HTTP+JSON hosting.</summary>
internal static class A2AExtensionNegotiation
{
    internal const string HeaderName = "A2A-Extensions";

    internal static async ValueTask<A2ARequestContext> NegotiateAsync(
        HttpContext httpContext,
        A2AExtensionOptions options,
        CancellationToken cancellationToken)
    {
        // Publish no state until the entire negotiation succeeds, including required extensions.
        httpContext.Features.Set<A2ARequestContext>(null);
        httpContext.Response.Headers.Remove(HeaderName);
        var requested = A2AExtensionIdentifiers.Parse(httpContext.Request.Headers[HeaderName]);
        var policy = options.CanActivate;
        var context = await options.Registry.NegotiateExtensionsAsync(
            requested,
            policy is null ? null : (extension, ct) => policy(httpContext, extension, ct),
            cancellationToken).ConfigureAwait(false);

        httpContext.Features.Set(context);
        if (context.ActivatedExtensions.Count > 0)
        {
            httpContext.Response.Headers[HeaderName] = string.Join(", ", context.ActivatedExtensions.Order(StringComparer.Ordinal));
        }

        return context;
    }

    internal static A2ACustomOperationContext CreateOperationContext(HttpContext httpContext)
    {
        var context = httpContext.Features.Get<A2ARequestContext>();
        object[] features = context is null ? [httpContext] : [httpContext, context];
        return new A2ACustomOperationContext(
            httpContext.RequestServices,
            features);
    }

}

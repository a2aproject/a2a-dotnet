namespace MultiAgentHost;

/// <summary>
/// Middleware that extracts the subdomain from the Host header and stores it
/// in the scoped <see cref="SubdomainContext"/> for downstream handlers.
/// </summary>
/// <remarks>
/// Supports two modes:
/// <list type="bullet">
///   <item>Real subdomains: <c>scheduler.platform.local</c> → subdomain = "scheduler"</item>
///   <item>Port-based dev fallback: <c>X-Agent-Subdomain</c> header for testing without DNS</item>
/// </list>
/// </remarks>
public sealed class SubdomainMiddleware(RequestDelegate next, string baseDomain)
{
    public Task InvokeAsync(HttpContext context)
    {
        var subdomainCtx = context.RequestServices.GetRequiredService<SubdomainContext>();

        // Priority 1: X-Agent-Subdomain header (for easy local testing without DNS)
        var headerValue = context.Request.Headers["X-Agent-Subdomain"].FirstOrDefault();
        if (!string.IsNullOrEmpty(headerValue))
        {
            subdomainCtx.Subdomain = headerValue;
            return next(context);
        }

        // Priority 2: Extract subdomain from Host header
        var host = context.Request.Host.Host;
        if (host.EndsWith(baseDomain, StringComparison.OrdinalIgnoreCase) && host.Length > baseDomain.Length)
        {
            // "scheduler.platform.local" with baseDomain ".platform.local" → "scheduler"
            var subdomain = host[..^baseDomain.Length];
            // Strip trailing dot if present
            subdomainCtx.Subdomain = subdomain.TrimEnd('.');
        }

        return next(context);
    }
}

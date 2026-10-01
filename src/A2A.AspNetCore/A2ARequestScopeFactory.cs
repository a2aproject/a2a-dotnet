using Microsoft.AspNetCore.Http;

namespace A2A.AspNetCore;

/// <summary>Creates request-specific A2A operation state for an ASP.NET Core request.</summary>
/// <param name="httpContext">The current HTTP context.</param>
/// <param name="cancellationToken">A cancellation token.</param>
/// <returns>The request scope.</returns>
public delegate ValueTask<A2ARequestScope> A2ARequestScopeFactory(
    HttpContext httpContext,
    CancellationToken cancellationToken);

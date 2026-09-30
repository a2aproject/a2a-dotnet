using Microsoft.AspNetCore.Http;

namespace A2A.AspNetCore;

internal sealed class A2ARequestScopeResult(
    IResult result,
    A2ARequestScope scope)
    : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        try
        {
            await result.ExecuteAsync(httpContext).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
}

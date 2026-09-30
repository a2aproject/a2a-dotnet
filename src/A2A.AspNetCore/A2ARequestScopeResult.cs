using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace A2A.AspNetCore;

internal sealed class A2ARequestScopeResult(
    IResult result,
    A2ARequestScope? scope,
    Activity? operationActivity = null)
    : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var previous = Activity.Current;
        Activity.Current = operationActivity ?? previous;
        try
        {
            try
            {
                await result.ExecuteAsync(httpContext).ConfigureAwait(false);
            }
            finally
            {
                if (scope is not null)
                {
                    await scope.DisposeAsync().ConfigureAwait(false);
                }
            }

            A2AOperationDiagnostics.SetOutcome(operationActivity, "success");
        }
        catch (Exception exception)
        {
            A2AOperationDiagnostics.SetError(operationActivity, exception);
            throw;
        }
        finally
        {
            operationActivity?.Dispose();
            Activity.Current = previous;
        }
    }
}

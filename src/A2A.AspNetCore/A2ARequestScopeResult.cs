using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace A2A.AspNetCore;

internal sealed class A2ARequestScopeResult(
    IResult result,
    A2ARequestScope? scope,
    Activity? operationActivity = null,
    Activity? transportActivity = null)
    : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var previous = Activity.Current;
        Activity.Current = operationActivity ?? transportActivity ?? previous;
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
            A2AOperationDiagnostics.SetError(operationActivity, exception, httpContext.RequestAborted);
            A2AAspNetCoreDiagnostics.RecordException(transportActivity, exception);
            throw;
        }
        finally
        {
            operationActivity?.Dispose();
            transportActivity?.Dispose();
            Activity.Current = previous;
        }
    }
}

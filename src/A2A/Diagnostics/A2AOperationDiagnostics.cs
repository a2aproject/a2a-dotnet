using System.Diagnostics;

namespace A2A;

internal static class A2AOperationDiagnostics
{
    internal static Activity? Start(
        ActivitySource activitySource,
        A2AOperationId operationId,
        A2AOperationKind kind,
        A2AOperationSource source,
        string role,
        string transport)
    {
        var activity = activitySource.StartActivity("a2a.operation", ActivityKind.Internal);
        activity?.SetTag("a2a.operation.id", operationId.Value);
        activity?.SetTag("a2a.operation.kind", kind == A2AOperationKind.Unary ? "unary" : "streaming");
        activity?.SetTag("a2a.operation.source", source == A2AOperationSource.Standard ? "standard" : "extension");
        activity?.SetTag("a2a.operation.role", role);
        activity?.SetTag("a2a.transport", transport);
        return activity;
    }

    internal static void SetOutcome(Activity? activity, string outcome)
    {
        // A handled transport error must not become a success when its response is written.
        if (outcome == "success" && activity?.GetTagItem("a2a.operation.outcome") is not null)
        {
            return;
        }

        activity?.SetTag("a2a.operation.outcome", outcome);
        activity?.SetStatus(outcome == "error" ? ActivityStatusCode.Error : ActivityStatusCode.Unset);
    }

    internal static void SetError(
        Activity? activity, Exception exception, CancellationToken cancellationToken = default) =>
        SetOutcome(activity, exception is OperationCanceledException && cancellationToken.IsCancellationRequested
            ? "cancelled" : "error");

    internal static async ValueTask DisposeAsync(
        IAsyncDisposable resource, Activity? activity, CancellationToken cancellationToken)
    {
        try
        {
            await resource.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            SetError(activity, exception, cancellationToken);
            throw;
        }
    }
}

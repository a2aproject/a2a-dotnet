using System.Diagnostics;

namespace A2A;

internal static class A2ACustomOperationDiagnostics
{
    internal static Activity? Start(A2AOperationId id, A2ACustomOperationKind kind)
    {
        var activity = A2ADiagnostics.Source.StartActivity("InvokeCustomOperation", ActivityKind.Internal);
        activity?.SetTag("a2a.operation.id", id.Value);
        activity?.SetTag("a2a.operation.kind", kind == A2ACustomOperationKind.Unary ? "unary" : "streaming");
        activity?.SetTag("a2a.operation.source", "custom");
        activity?.SetTag("a2a.operation.role", "server");
        return activity;
    }

    internal static void SetSuccess(Activity? activity) =>
        activity?.SetTag("a2a.operation.outcome", "success");

    internal static void SetCancelled(Activity? activity) =>
        activity?.SetTag("a2a.operation.outcome", "cancelled");

    internal static void SetError(Activity? activity, Exception exception)
    {
        activity?.SetTag("a2a.operation.outcome", "error");
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
    }
}

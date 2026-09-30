using System.Diagnostics;

namespace A2A.AspNetCore;

internal readonly record struct A2AOperationDiagnosticContext(
    A2AOperationId OperationId,
    A2AOperationKind Kind,
    A2AOperationSource Source)
{
    internal Activity? Start() =>
        A2AOperationDiagnostics.Start(OperationId, Kind, Source);
}

internal static class A2AOperationDiagnostics
{
    internal static Activity? Start(
        A2AOperationId operationId,
        A2AOperationKind kind,
        A2AOperationSource source)
    {
        var activity = A2AAspNetCoreDiagnostics.Source.StartActivity(
            "a2a.operation",
            ActivityKind.Internal);
        activity?.SetTag("a2a.operation.id", operationId.Value);
        activity?.SetTag(
            "a2a.operation.kind",
            kind == A2AOperationKind.Unary ? "unary" : "streaming");
        activity?.SetTag(
            "a2a.operation.source",
            source == A2AOperationSource.Standard ? "standard" : "extension");
        activity?.SetTag("a2a.operation.role", "server");
        activity?.SetTag("a2a.transport", "jsonrpc");
        return activity;
    }

    internal static void SetOutcome(Activity? activity, string outcome)
    {
        activity?.SetTag("a2a.operation.outcome", outcome);
        if (outcome == "error")
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }
    }

    internal static void SetError(Activity? activity, Exception exception)
    {
        activity?.SetTag("a2a.operation.outcome", "error");
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.AddException(exception);
    }
}

using System.Diagnostics;

namespace A2A.AspNetCore;

internal readonly record struct A2AOperationDiagnosticContext(
    A2AOperationId OperationId,
    A2AOperationKind Kind,
    A2AOperationSource Source,
    string Transport = "jsonrpc")
{
    internal Activity? Start() =>
        A2AOperationDiagnostics.Start(
            A2AAspNetCoreDiagnostics.Source, OperationId, Kind, Source, "server", Transport);
}

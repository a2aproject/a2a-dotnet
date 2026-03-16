using System.Diagnostics;

namespace A2A.AspNetCore;

/// <summary>
/// Diagnostics for the A2A ASP.NET Core integration layer.
/// </summary>
internal static class A2AAspNetCoreDiagnostics
{
    private static readonly string Version =
        typeof(A2AAspNetCoreDiagnostics).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    /// <summary>Activity source for HTTP/JSON-RPC protocol processing.</summary>
    internal static readonly ActivitySource Source = new("A2A.AspNetCore", Version);
}

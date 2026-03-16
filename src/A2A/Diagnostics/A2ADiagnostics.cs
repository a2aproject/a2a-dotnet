using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace A2A;

/// <summary>
/// Centralized diagnostics for the A2A SDK. Follows Azure SDK conventions:
/// single ActivitySource and Meter per library, named after the NuGet package.
/// </summary>
internal static class A2ADiagnostics
{
    private static readonly string Version =
        typeof(A2ADiagnostics).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    /// <summary>Activity source for all A2A core library spans.</summary>
    internal static readonly ActivitySource Source = new("A2A", Version);

    /// <summary>Meter for all A2A core library metrics.</summary>
    internal static readonly Meter Meter = new("A2A", Version);

    // ─── Metrics Instruments ───

    internal static readonly Counter<long> RequestCount =
        Meter.CreateCounter<long>("a2a.server.request.count",
            description: "Number of A2A requests processed");

    internal static readonly Histogram<double> RequestDuration =
        Meter.CreateHistogram<double>("a2a.server.request.duration",
            "ms", "Duration of A2A request processing");

    internal static readonly Counter<long> ErrorCount =
        Meter.CreateCounter<long>("a2a.server.error.count",
            description: "Number of A2A errors");

    internal static readonly Counter<long> TaskCreatedCount =
        Meter.CreateCounter<long>("a2a.server.task.created",
            description: "Number of tasks created");

    internal static readonly Histogram<double> StreamEventCount =
        Meter.CreateHistogram<double>("a2a.server.stream.event.count",
            description: "Events per streaming request");

    // ─── Client Metrics ───

    internal static readonly Counter<long> ClientRequestCount =
        Meter.CreateCounter<long>("a2a.client.request.count",
            description: "Number of A2A client requests sent");

    internal static readonly Histogram<double> ClientRequestDuration =
        Meter.CreateHistogram<double>("a2a.client.request.duration",
            "ms", "Duration of A2A client request processing");

    internal static readonly Counter<long> ClientErrorCount =
        Meter.CreateCounter<long>("a2a.client.error.count",
            description: "Number of A2A client errors");

    internal static readonly Histogram<double> ClientStreamEventCount =
        Meter.CreateHistogram<double>("a2a.client.stream.event.count",
            description: "Events per streaming client request");
}

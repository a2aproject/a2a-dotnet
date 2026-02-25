namespace A2A;

/// <summary>
/// An event with its position in the per-task event stream.
/// </summary>
/// <param name="Version">0-based sequence number within the task's event log.</param>
/// <param name="Event">The event payload.</param>
public readonly record struct EventEnvelope(long Version, StreamResponse Event);

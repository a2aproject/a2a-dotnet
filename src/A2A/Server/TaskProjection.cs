namespace A2A;

/// <summary>
/// Pure projection functions for materializing <see cref="AgentTask"/> state
/// from a stream of <see cref="StreamResponse"/> events.
/// </summary>
/// <remarks>
/// SQL or persistent store implementors can reuse <see cref="Apply"/> inside their
/// <see cref="IEventStore.AppendAsync"/> to maintain a projected read model alongside
/// the event log, enabling efficient <see cref="ITaskEventStore.GetTaskAsync"/> and
/// <see cref="ITaskEventStore.ListTasksAsync"/> without full event replay.
/// </remarks>
public static class TaskProjection
{
    /// <summary>
    /// Apply a single event to an AgentTask state, returning the updated state.
    /// </summary>
    /// <param name="current">The current task state, or null if no events have been applied.</param>
    /// <param name="event">The event to apply.</param>
    public static AgentTask? Apply(AgentTask? current, StreamResponse @event)
    {
        if (@event.Task is { } task)
            return task;

        if (current is null)
            return current;

        if (@event.StatusUpdate is { } su)
            return ApplyStatus(current, su);

        if (@event.ArtifactUpdate is { } au)
            return ApplyArtifact(current, au);

        if (@event.Message is { } msg)
        {
            if (current is not null)
            {
                (current.History ??= []).Add(msg);
            }
            return current;
        }

        return current;
    }

    /// <summary>
    /// Replay an entire event stream to produce the current task state.
    /// </summary>
    /// <param name="events">The event stream to replay.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<AgentTask?> ReplayAsync(
        IAsyncEnumerable<EventEnvelope> events,
        CancellationToken cancellationToken = default)
    {
        AgentTask? state = null;
        await foreach (var envelope in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            state = Apply(state, envelope.Event);
        }
        return state;
    }

    private static AgentTask ApplyStatus(AgentTask current, TaskStatusUpdateEvent su)
    {
        // Move superseded status.message to history (aligned with Python SDK behavior).
        if (current.Status.Message is not null)
        {
            (current.History ??= []).Add(current.Status.Message);
        }
        current.Status = su.Status;
        return current;
    }

    private static AgentTask ApplyArtifact(AgentTask current, TaskArtifactUpdateEvent au)
    {
        current.Artifacts ??= [];
        var artifactId = au.Artifact.ArtifactId;

        if (!au.Append)
        {
            // append=false: add new or replace existing by artifactId
            var idx = current.Artifacts.FindIndex(a => a.ArtifactId == artifactId);
            if (idx >= 0)
                current.Artifacts[idx] = au.Artifact;
            else
                current.Artifacts.Add(au.Artifact);
        }
        else
        {
            // append=true: extend existing artifact's parts list, or add if not found
            var existing = current.Artifacts.FirstOrDefault(a => a.ArtifactId == artifactId);
            if (existing is not null)
            {
                existing.Parts.AddRange(au.Artifact.Parts);
            }
            else
            {
                current.Artifacts.Add(au.Artifact);
            }
        }
        return current;
    }
}

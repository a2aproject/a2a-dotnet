namespace A2A;

/// <summary>
/// Pure projection functions for materializing <see cref="AgentTask"/> state
/// from a stream of <see cref="StreamResponse"/> events.
/// </summary>
/// <remarks>
/// <see cref="A2AServer"/> uses <see cref="Apply"/> to mutate task state before
/// persisting via <see cref="ITaskStore.SaveTaskAsync"/>. Store implementors do
/// not need to call this directly.
/// </remarks>
public static class TaskProjection
{
    /// <summary>
    /// Apply a single event to an AgentTask state, returning the updated state.
    /// </summary>
    /// <param name="current">The current task state, or null if no events have been applied.</param>
    /// <param name="streamEvent">The event to apply.</param>
    public static AgentTask? Apply(AgentTask? current, StreamResponse streamEvent)
    {
        if (streamEvent.Task is { } task)
            return task;

        if (current is null)
            return current;

        if (streamEvent.StatusUpdate is { } su)
            return ApplyStatus(current, su);

        if (streamEvent.ArtifactUpdate is { } au)
            return ApplyArtifact(current, au);

        if (streamEvent.Message is { } msg)
        {
            if (current is not null)
            {
                (current.History ??= []).Add(msg);
            }
            return current;
        }

        return current;
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

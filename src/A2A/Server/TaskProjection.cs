using System.Text.Json;

namespace A2A;

/// <summary>
/// Pure projection functions for materializing <see cref="AgentTask"/> state
/// from a stream of <see cref="StreamResponse"/> events.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Apply"/> always returns a new <see cref="AgentTask"/> instance (or null).
/// The incoming task is never mutated, making the projection inherently safe regardless
/// of whether <see cref="ITaskStore"/> implementations clone on read.
/// </para>
/// <para>
/// Sub-objects such as <see cref="Artifact"/> instances are also replaced rather than
/// mutated in-place, so concurrent readers holding references to prior artifacts or
/// history lists see consistent snapshots.
/// </para>
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

        // Shallow copy so the caller's AgentTask reference is never mutated.
        // Artifacts list is copied because ApplyArtifact mutates it (Add/index-set).
        // History is not copied — every branch that touches it replaces the reference entirely.
        var result = new AgentTask
        {
            Id = current.Id,
            ContextId = current.ContextId,
            Status = current.Status,
            History = current.History,
            Artifacts = current.Artifacts is not null ? [.. current.Artifacts] : null,
            Metadata = current.Metadata,
        };

        if (streamEvent.StatusUpdate is { } su)
            return ApplyStatus(result, su);

        if (streamEvent.ArtifactUpdate is { } au)
            return ApplyArtifact(result, au);

        if (streamEvent.Message is { } msg)
            return ApplyMessage(result, msg);

        return result;
    }

    private static AgentTask ApplyMessage(AgentTask current, Message msg)
    {
        GuardNotTerminal(current);
        current.History = [.. (current.History ?? []), msg];
        return current;
    }

    private static AgentTask ApplyStatus(AgentTask current, TaskStatusUpdateEvent su)
    {
        GuardNotTerminal(current);

        // Move superseded status.message to history (aligned with Python SDK behavior).
        if (current.Status.Message is not null)
        {
            current.History = [.. (current.History ?? []), current.Status.Message];
        }
        current.Status = su.Status;
        return current;
    }

    /// <summary>
    /// Terminal states are final: a completed/canceled/failed/rejected task must not be
    /// overwritten by a later status update or accept further messages (BUG-43).
    /// Guards the projection against state-machine violations that bypass the
    /// request-level <see cref="A2AServer"/> terminal checks (e.g. a misbehaving agent
    /// handler emitting events after a terminal status).
    /// </summary>
    /// <param name="current">The current task state being projected.</param>
    /// <exception cref="A2AException">
    /// Thrown with <see cref="A2AErrorCode.UnsupportedOperation"/> when the task is terminal.
    /// </exception>
    private static void GuardNotTerminal(AgentTask current)
    {
        if (current.Status.State.IsTerminal())
        {
            throw new A2AException(
                $"Task '{current.Id}' is in a terminal state ({current.Status.State}) and cannot be modified.",
                A2AErrorCode.UnsupportedOperation);
        }
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
            // append=true: build new artifact with merged data — never mutate existing
            var existing = current.Artifacts.FirstOrDefault(a => a.ArtifactId == artifactId);
            if (existing is not null)
            {
                var merged = new Artifact
                {
                    ArtifactId = existing.ArtifactId,
                    Name = !string.IsNullOrEmpty(au.Artifact.Name) ? au.Artifact.Name : existing.Name,
                    Description = !string.IsNullOrEmpty(au.Artifact.Description)
                        ? au.Artifact.Description : existing.Description,
                    Parts = [.. existing.Parts, .. au.Artifact.Parts],
                    Metadata = MergeMetadata(existing.Metadata, au.Artifact.Metadata),
                    Extensions = MergeExtensions(existing.Extensions, au.Artifact.Extensions),
                };

                var idx = current.Artifacts.IndexOf(existing);
                current.Artifacts[idx] = merged;
            }
            else
            {
                current.Artifacts.Add(au.Artifact);
            }
        }
        return current;
    }

    private static Dictionary<string, JsonElement>? MergeMetadata(
        Dictionary<string, JsonElement>? existing, Dictionary<string, JsonElement>? incoming)
    {
        if (incoming is null) return existing;
        var result = existing is not null ? new Dictionary<string, JsonElement>(existing) : new();
        foreach (var kvp in incoming)
            result[kvp.Key] = kvp.Value;
        return result;
    }

    private static List<string>? MergeExtensions(List<string>? existing, List<string>? incoming)
    {
        if (incoming is null) return existing;
        if (existing is null) return [.. incoming];
        var seen = new HashSet<string>(existing);
        var result = new List<string>(existing);
        foreach (var ext in incoming)
        {
            if (seen.Add(ext))
                result.Add(ext);
        }
        return result;
    }
}

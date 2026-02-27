using System.Text.Json;

namespace A2A;

/// <summary>
/// Provides helper methods for applying artifact updates to tasks.
/// </summary>
/// <remarks>
/// Centralizes the merge logic for <see cref="TaskArtifactUpdateEvent"/> so that all
/// <see cref="ITaskStore"/> implementations produce consistent results.
/// </remarks>
public static class ArtifactHelper
{
    /// <summary>
    /// Applies an artifact update to a task's artifact list using delta semantics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <paramref name="append"/> is true, the update is treated as a delta:
    /// <list type="bullet">
    ///   <item><description><b>Parts</b>: Appended to the existing parts list.</description></item>
    ///   <item><description><b>Metadata</b>: Upserted — new keys are added, existing keys are updated.</description></item>
    ///   <item><description><b>Extensions</b>: Appended — new values are added, duplicates are ignored.</description></item>
    ///   <item><description><b>Name/Description</b>: Updated if non-null/non-empty in the incoming artifact.</description></item>
    /// </list>
    /// If no existing artifact with the same <see cref="Artifact.ArtifactId"/> is found, a new artifact is created.
    /// </para>
    /// <para>
    /// When <paramref name="append"/> is false, the incoming artifact replaces any existing artifact
    /// with the same <see cref="Artifact.ArtifactId"/>, or is added if none exists.
    /// </para>
    /// </remarks>
    /// <param name="task">The task to update. Its <see cref="AgentTask.Artifacts"/> list will be modified in place.</param>
    /// <param name="artifact">The artifact or artifact chunk to apply.</param>
    /// <param name="append">If true, apply delta semantics. If false, replace.</param>
    public static void ApplyArtifactUpdate(AgentTask task, Artifact artifact, bool append)
    {
        task.Artifacts ??= [];

        if (append)
        {
            var existingIndex = task.Artifacts.FindIndex(a => a.ArtifactId == artifact.ArtifactId);
            if (existingIndex >= 0)
            {
                var existing = task.Artifacts[existingIndex];

                // Parts: append
                var mergedParts = new List<Part>(existing.Parts);
                mergedParts.AddRange(artifact.Parts);

                // Metadata: upsert
                Dictionary<string, JsonElement>? mergedMetadata = null;
                if (existing.Metadata != null || artifact.Metadata != null)
                {
                    mergedMetadata = existing.Metadata != null ? new(existing.Metadata) : [];
                    if (artifact.Metadata != null)
                    {
                        foreach (var kvp in artifact.Metadata)
                        {
                            mergedMetadata[kvp.Key] = kvp.Value;
                        }
                    }
                }

                // Extensions: append (deduplicated)
                List<string>? mergedExtensions = null;
                if (existing.Extensions != null || artifact.Extensions != null)
                {
                    mergedExtensions = existing.Extensions != null ? [.. existing.Extensions] : [];
                    if (artifact.Extensions != null)
                    {
                        foreach (var ext in artifact.Extensions)
                        {
                            if (!mergedExtensions.Contains(ext))
                            {
                                mergedExtensions.Add(ext);
                            }
                        }
                    }
                }

                // Build new artifact (immutable update)
                task.Artifacts[existingIndex] = new Artifact
                {
                    ArtifactId = artifact.ArtifactId,
                    Name = !string.IsNullOrEmpty(artifact.Name) ? artifact.Name : existing.Name,
                    Description = !string.IsNullOrEmpty(artifact.Description) ? artifact.Description : existing.Description,
                    Parts = mergedParts,
                    Metadata = mergedMetadata,
                    Extensions = mergedExtensions
                };
            }
            else
            {
                // No existing artifact — create new copy
                task.Artifacts.Add(CopyArtifact(artifact));
            }
        }
        else
        {
            // Replace or add
            var artifactCopy = CopyArtifact(artifact);
            var existingIndex = task.Artifacts.FindIndex(a => a.ArtifactId == artifact.ArtifactId);
            if (existingIndex >= 0)
            {
                task.Artifacts[existingIndex] = artifactCopy;
            }
            else
            {
                task.Artifacts.Add(artifactCopy);
            }
        }
    }

    /// <summary>
    /// Creates a defensive copy of an artifact to prevent external mutation of stored state.
    /// </summary>
    /// <param name="artifact">The artifact to copy.</param>
    internal static Artifact CopyArtifact(Artifact artifact) => new()
    {
        ArtifactId = artifact.ArtifactId,
        Name = artifact.Name,
        Description = artifact.Description,
        Parts = [.. artifact.Parts],
        Metadata = artifact.Metadata != null ? new(artifact.Metadata) : null,
        Extensions = artifact.Extensions != null ? [.. artifact.Extensions] : null
    };
}

using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace A2A.AspNetCore.Caching;

/// <summary>
/// Distributed cache implementation of task store.
/// </summary>
/// <param name="cache">The <see cref="IDistributedCache"/> used to store tasks.</param>
public class DistributedCacheTaskStore(IDistributedCache cache)
    : ITaskStore
{
    /// <inheritdoc/>
    public async Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(taskId))
        {
            throw new ArgumentNullException(nameof(taskId));
        }
        var bytes = await cache.GetAsync(BuildTaskCacheKey(taskId), cancellationToken).ConfigureAwait(false);
        if (bytes == null || bytes.Length < 1)
        {
            return null;
        }
        return JsonSerializer.Deserialize(bytes, A2AJsonUtilities.JsonContext.Default.AgentTask);
    }

    /// <inheritdoc/>
    public async Task<TaskPushNotificationConfig?> GetPushNotificationAsync(string taskId, string notificationConfigId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(taskId))
        {
            throw new ArgumentNullException(nameof(taskId));
        }
        var bytes = await cache.GetAsync(BuildPushNotificationsCacheKey(taskId), cancellationToken).ConfigureAwait(false);
        if (bytes == null || bytes.Length < 1)
        {
            return null;
        }
        var pushNotificationConfigs = JsonSerializer.Deserialize(bytes, A2AJsonUtilities.JsonContext.Default.ListTaskPushNotificationConfig);
        if (pushNotificationConfigs == null || pushNotificationConfigs.Count < 1)
        {
            return null;
        }
        return pushNotificationConfigs.FirstOrDefault(config => config.PushNotificationConfig.Id == notificationConfigId);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<TaskPushNotificationConfig>> GetPushNotificationsAsync(string taskId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await cache.GetAsync(BuildPushNotificationsCacheKey(taskId), cancellationToken).ConfigureAwait(false);
        if (bytes == null || bytes.Length < 1)
        {
            return [];
        }
        return JsonSerializer.Deserialize(bytes, A2AJsonUtilities.JsonContext.Default.ListTaskPushNotificationConfig) ?? [];
    }

    /// <inheritdoc/>
    public async Task<AgentTaskStatus> UpdateStatusAsync(string taskId, TaskState status, AgentMessage? message = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(taskId))
        {
            throw new ArgumentNullException(nameof(taskId));
        }
        var cacheKey = BuildTaskCacheKey(taskId);
        var bytes = await cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        if (bytes == null || bytes.Length < 1)
        {
            throw new A2AException($"Task with ID '{taskId}' not found in cache.", A2AErrorCode.TaskNotFound);
        }
        var task = JsonSerializer.Deserialize(bytes, A2AJsonUtilities.JsonContext.Default.AgentTask) ?? throw new InvalidDataException("Task data from cache is corrupt.");
        task.Status = task.Status with
        {
            State = status,
            Message = message,
            Timestamp = DateTimeOffset.UtcNow
        };
        bytes = JsonSerializer.SerializeToUtf8Bytes(task, A2AJsonUtilities.JsonContext.Default.AgentTask);
        await cache.SetAsync(cacheKey, bytes, cancellationToken).ConfigureAwait(false);
        return task.Status;
    }

    /// <inheritdoc/>
    public async Task SetTaskAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(task, A2AJsonUtilities.JsonContext.Default.AgentTask);
        await cache.SetAsync(BuildTaskCacheKey(task.Id), bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SetPushNotificationConfigAsync(TaskPushNotificationConfig pushNotificationConfig, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pushNotificationConfig is null)
        {
            throw new ArgumentNullException(nameof(pushNotificationConfig));
        }

        if (string.IsNullOrWhiteSpace(pushNotificationConfig.TaskId))
        {
            throw new ArgumentException("Task ID cannot be null or empty.", nameof(pushNotificationConfig));
        }
        var bytes = await cache.GetAsync(BuildPushNotificationsCacheKey(pushNotificationConfig.TaskId), cancellationToken).ConfigureAwait(false);
        var pushNotificationConfigs = bytes == null ? [] : JsonSerializer.Deserialize(bytes, A2AJsonUtilities.JsonContext.Default.ListTaskPushNotificationConfig) ?? [];
        pushNotificationConfigs.RemoveAll(c => c.PushNotificationConfig.Id == pushNotificationConfig.PushNotificationConfig.Id);
        pushNotificationConfigs.Add(pushNotificationConfig);
        bytes = JsonSerializer.SerializeToUtf8Bytes(pushNotificationConfigs, A2AJsonUtilities.JsonContext.Default.ListTaskPushNotificationConfig);
        await cache.SetAsync(BuildPushNotificationsCacheKey(pushNotificationConfig.TaskId), bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpdateArtifactAsync(string taskId, Artifact artifact, bool append, bool lastChunk, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(taskId))
        {
            throw new A2AException("Invalid task ID", new ArgumentNullException(nameof(taskId)), A2AErrorCode.InvalidParams);
        }

        if (artifact is null)
        {
            throw new ArgumentNullException(nameof(artifact));
        }

        if (string.IsNullOrEmpty(artifact.ArtifactId))
        {
            throw new A2AException("Artifact must have an artifactId for streaming.", A2AErrorCode.InvalidParams);
        }

        var cacheKey = BuildTaskCacheKey(taskId);
        var bytes = await cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        if (bytes == null || bytes.Length < 1)
        {
            throw new A2AException("Task not found.", A2AErrorCode.TaskNotFound);
        }

        var task = JsonSerializer.Deserialize(bytes, A2AJsonUtilities.JsonContext.Default.AgentTask) ?? throw new InvalidDataException("Task data from cache is corrupt.");
        task.Artifacts ??= [];

        // Load sealed artifacts from cache
        var sealedKey = BuildSealedArtifactsCacheKey(taskId);
        var sealedBytes = await cache.GetAsync(sealedKey, cancellationToken).ConfigureAwait(false);
        var sealedArtifacts = sealedBytes != null && sealedBytes.Length > 0
            ? JsonSerializer.Deserialize(sealedBytes, A2AJsonUtilities.JsonContext.Default.HashSetString) ?? []
            : [];

        // Reject updates to sealed artifacts
        if (sealedArtifacts.Contains(artifact.ArtifactId))
        {
            throw new A2AException(
                $"Artifact '{artifact.ArtifactId}' has been sealed (lastChunk=true was set). " +
                "Once an artifact is sealed, it cannot be updated further.",
                A2AErrorCode.InvalidRequest);
        }

        if (append)
        {
            var existingArtifact = task.Artifacts.FirstOrDefault(a => a.ArtifactId == artifact.ArtifactId);
            if (existingArtifact != null)
            {
                existingArtifact.Parts.AddRange(artifact.Parts);
                if (!string.IsNullOrEmpty(artifact.Name))
                {
                    existingArtifact.Name = artifact.Name;
                }
                if (!string.IsNullOrEmpty(artifact.Description))
                {
                    existingArtifact.Description = artifact.Description;
                }
                if (artifact.Metadata != null && artifact.Metadata.Count > 0)
                {
                    existingArtifact.Metadata ??= [];
                    foreach (var kvp in artifact.Metadata)
                    {
                        existingArtifact.Metadata[kvp.Key] = kvp.Value;
                    }
                }
                if (artifact.Extensions != null && artifact.Extensions.Count > 0)
                {
                    existingArtifact.Extensions ??= [];
                    foreach (var ext in artifact.Extensions)
                    {
                        if (!existingArtifact.Extensions.Contains(ext))
                        {
                            existingArtifact.Extensions.Add(ext);
                        }
                    }
                }
            }
            else
            {
                task.Artifacts.Add(new Artifact
                {
                    ArtifactId = artifact.ArtifactId,
                    Name = artifact.Name,
                    Description = artifact.Description,
                    Parts = [.. artifact.Parts],
                    Metadata = artifact.Metadata != null ? new(artifact.Metadata) : null,
                    Extensions = artifact.Extensions != null ? [.. artifact.Extensions] : null
                });
            }
        }
        else
        {
            var artifactCopy = new Artifact
            {
                ArtifactId = artifact.ArtifactId,
                Name = artifact.Name,
                Description = artifact.Description,
                Parts = [.. artifact.Parts],
                Metadata = artifact.Metadata != null ? new(artifact.Metadata) : null,
                Extensions = artifact.Extensions != null ? [.. artifact.Extensions] : null
            };

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

        // Seal if lastChunk
        if (lastChunk)
        {
            sealedArtifacts.Add(artifact.ArtifactId);
        }

        // Save task and sealed artifacts
        bytes = JsonSerializer.SerializeToUtf8Bytes(task, A2AJsonUtilities.JsonContext.Default.AgentTask);
        await cache.SetAsync(cacheKey, bytes, cancellationToken).ConfigureAwait(false);

        var sealedBytesOut = JsonSerializer.SerializeToUtf8Bytes(sealedArtifacts, A2AJsonUtilities.JsonContext.Default.HashSetString);
        await cache.SetAsync(sealedKey, sealedBytesOut, cancellationToken).ConfigureAwait(false);
    }

    static string BuildTaskCacheKey(string taskId) => $"task:{taskId}";

    static string BuildPushNotificationsCacheKey(string taskId) => $"task-push-notification:{taskId}";

    static string BuildSealedArtifactsCacheKey(string taskId) => $"task-sealed-artifacts:{taskId}";
}

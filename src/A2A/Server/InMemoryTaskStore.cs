using System.Collections.Concurrent;

namespace A2A;

/// <summary>
/// In-memory implementation of task store for development and testing.
/// </summary>
public sealed class InMemoryTaskStore : ITaskStore
{
    private readonly ConcurrentDictionary<string, AgentTask> _taskCache = [];
    // PushNotificationConfig.Id is optional, so there can be multiple configs with no Id.
    // Since we want to maintain order of insertion and thread safety, we use a ConcurrentQueue.
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TaskPushNotificationConfig>> _pushNotificationCache = [];
    // Track sealed artifacts per task
    private readonly ConcurrentDictionary<string, HashSet<string>> _sealedArtifacts = [];
    // Per-task locks for artifact update atomicity
    private readonly ConcurrentDictionary<string, object> _taskArtifactLocks = [];

    /// <inheritdoc />
    public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AgentTask?>(cancellationToken);
        }

        return string.IsNullOrEmpty(taskId)
            ? Task.FromException<AgentTask?>(new ArgumentNullException(nameof(taskId)))
            : Task.FromResult(_taskCache.TryGetValue(taskId, out var task) ? task : null);
    }

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig?> GetPushNotificationAsync(string taskId, string notificationConfigId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<TaskPushNotificationConfig?>(cancellationToken);
        }

        if (string.IsNullOrEmpty(taskId))
        {
            return Task.FromException<TaskPushNotificationConfig?>(new ArgumentNullException(nameof(taskId)));
        }

        if (!_pushNotificationCache.TryGetValue(taskId, out var pushNotificationConfigs))
        {
            return Task.FromResult<TaskPushNotificationConfig?>(null);
        }

        var pushNotificationConfig = pushNotificationConfigs.FirstOrDefault(config => config.PushNotificationConfig.Id == notificationConfigId);

        return Task.FromResult<TaskPushNotificationConfig?>(pushNotificationConfig);
    }

    /// <inheritdoc />
    public Task<AgentTaskStatus> UpdateStatusAsync(string taskId, TaskState status, AgentMessage? message = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AgentTaskStatus>(cancellationToken);
        }

        if (string.IsNullOrEmpty(taskId))
        {
            return Task.FromException<AgentTaskStatus>(new A2AException("Invalid task ID", new ArgumentNullException(nameof(taskId)), A2AErrorCode.InvalidParams));
        }

        if (!_taskCache.TryGetValue(taskId, out var task))
        {
            return Task.FromException<AgentTaskStatus>(new A2AException("Task not found.", A2AErrorCode.TaskNotFound));
        }

        return Task.FromResult(task.Status = task.Status with
        {
            Message = message,
            State = status,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <inheritdoc />
    public Task SetTaskAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (task is null)
        {
            return Task.FromException(new ArgumentNullException(nameof(task)));
        }

        if (string.IsNullOrEmpty(task.Id))
        {
            return Task.FromException(new A2AException("Invalid task ID", A2AErrorCode.InvalidParams));
        }

        _taskCache[task.Id] = task;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetPushNotificationConfigAsync(TaskPushNotificationConfig pushNotificationConfig, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (pushNotificationConfig is null)
        {
            return Task.FromException(new ArgumentNullException(nameof(pushNotificationConfig)));
        }

        var pushNotificationConfigs = _pushNotificationCache.GetOrAdd(pushNotificationConfig.TaskId, _ => []);
        pushNotificationConfigs.Enqueue(pushNotificationConfig);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IEnumerable<TaskPushNotificationConfig>> GetPushNotificationsAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IEnumerable<TaskPushNotificationConfig>>(cancellationToken);
        }

        if (!_pushNotificationCache.TryGetValue(taskId, out var pushNotificationConfigs))
        {
            return Task.FromResult<IEnumerable<TaskPushNotificationConfig>>([]);
        }

        return Task.FromResult<IEnumerable<TaskPushNotificationConfig>>(pushNotificationConfigs);
    }

    /// <inheritdoc />
    public Task UpdateArtifactAsync(string taskId, Artifact artifact, bool append, bool lastChunk, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (string.IsNullOrEmpty(taskId))
        {
            return Task.FromException(new A2AException("Invalid task ID", new ArgumentNullException(nameof(taskId)), A2AErrorCode.InvalidParams));
        }

        if (artifact is null)
        {
            return Task.FromException(new ArgumentNullException(nameof(artifact)));
        }

        if (string.IsNullOrEmpty(artifact.ArtifactId))
        {
            return Task.FromException(new A2AException("Artifact must have an artifactId for streaming.", A2AErrorCode.InvalidParams));
        }

        if (!_taskCache.TryGetValue(taskId, out var task))
        {
            return Task.FromException(new A2AException("Task not found.", A2AErrorCode.TaskNotFound));
        }

        // Lock per-task to ensure atomicity of sealed check + artifact mutation
        var taskLock = _taskArtifactLocks.GetOrAdd(taskId, _ => new object());
        lock (taskLock)
        {
            task.Artifacts ??= [];

            var sealedArtifacts = _sealedArtifacts.GetOrAdd(taskId, _ => []);

            // Reject updates to sealed artifacts
            if (sealedArtifacts.Contains(artifact.ArtifactId))
            {
                return Task.FromException(new A2AException(
                    $"Artifact '{artifact.ArtifactId}' has been sealed (lastChunk=true was set). " +
                    "Once an artifact is sealed, it cannot be updated further.",
                    A2AErrorCode.InvalidRequest));
            }

            if (append)
            {
                var existingIndex = task.Artifacts.FindIndex(a => a.ArtifactId == artifact.ArtifactId);
                if (existingIndex >= 0)
                {
                    // Build a new artifact with merged data to avoid mutating stored references
                    var existing = task.Artifacts[existingIndex];
                    var mergedParts = new List<Part>(existing.Parts);
                    mergedParts.AddRange(artifact.Parts);

                    Dictionary<string, System.Text.Json.JsonElement>? mergedMetadata = null;
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
                    // No existing artifact, add as new copy
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
                // Replace existing artifact
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
        }

        return Task.CompletedTask;
    }
}
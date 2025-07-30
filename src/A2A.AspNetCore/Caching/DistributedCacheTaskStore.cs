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
    public async Task<AgentTaskStatus> UpdateStatusAsync(string taskId, TaskState status, Message? message = null, CancellationToken cancellationToken = default)
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

    static string BuildTaskCacheKey(string taskId) => $"task:{taskId}";

    static string BuildPushNotificationsCacheKey(string taskId) => $"task-push-notification:{taskId}";
}

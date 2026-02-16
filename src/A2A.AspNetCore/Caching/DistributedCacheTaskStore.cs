using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace A2A.AspNetCore.Caching;

/// <summary>
/// Distributed cache implementation of task store.
/// </summary>
/// <param name="cache">The distributed cache instance.</param>
public class DistributedCacheTaskStore(IDistributedCache cache)
    : ITaskStore
{
    /// <inheritdoc />
    public async Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(taskId))
        {
            throw new ArgumentNullException(nameof(taskId));
        }

        var bytes = await cache.GetAsync(BuildTaskCacheKey(taskId), cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length < 1)
        {
            return null;
        }

        return JsonSerializer.Deserialize(bytes, A2AJsonUtilities.JsonContext.Default.AgentTask);
    }

    /// <inheritdoc />
    public async Task<AgentTask> SetTaskAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(task, A2AJsonUtilities.JsonContext.Default.AgentTask);
        await cache.SetAsync(BuildTaskCacheKey(task.Id), bytes, cancellationToken).ConfigureAwait(false);
        return task;
    }

    /// <inheritdoc />
    public async Task<AgentTask> UpdateStatusAsync(string taskId, TaskStatus status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(taskId))
        {
            throw new ArgumentNullException(nameof(taskId));
        }

        var cacheKey = BuildTaskCacheKey(taskId);
        var bytes = await cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length < 1)
        {
            throw new A2AException($"Task with ID '{taskId}' not found in cache.", A2AErrorCode.TaskNotFound);
        }

        var task = JsonSerializer.Deserialize(bytes, A2AJsonUtilities.JsonContext.Default.AgentTask)
            ?? throw new InvalidDataException("Task data from cache is corrupt.");
        task.Status = status;
        bytes = JsonSerializer.SerializeToUtf8Bytes(task, A2AJsonUtilities.JsonContext.Default.AgentTask);
        await cache.SetAsync(cacheKey, bytes, cancellationToken).ConfigureAwait(false);
        return task;
    }

    /// <inheritdoc />
    public async Task<AgentTask> AppendHistoryAsync(string taskId, Message message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(taskId))
        {
            throw new ArgumentNullException(nameof(taskId));
        }

        var cacheKey = BuildTaskCacheKey(taskId);
        var bytes = await cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length < 1)
        {
            throw new A2AException($"Task with ID '{taskId}' not found in cache.", A2AErrorCode.TaskNotFound);
        }

        var task = JsonSerializer.Deserialize(bytes, A2AJsonUtilities.JsonContext.Default.AgentTask)
            ?? throw new InvalidDataException("Task data from cache is corrupt.");
        (task.History ??= []).Add(message);
        bytes = JsonSerializer.SerializeToUtf8Bytes(task, A2AJsonUtilities.JsonContext.Default.AgentTask);
        await cache.SetAsync(cacheKey, bytes, cancellationToken).ConfigureAwait(false);
        return task;
    }

    /// <inheritdoc />
    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        // Distributed cache does not support listing/querying — return empty
        return Task.FromResult(new ListTasksResponse { Tasks = [] });
    }

    private static string BuildTaskCacheKey(string taskId) => $"task:{taskId}";
}

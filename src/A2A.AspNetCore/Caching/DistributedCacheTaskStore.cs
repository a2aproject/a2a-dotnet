using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace A2A.AspNetCore.Caching;

/// <summary>
/// Distributed cache implementation of task store.
/// </summary>
/// <remarks>
/// <para>This store uses <see cref="IDistributedCache"/> for task persistence,
/// suitable for multi-instance deployments (e.g., Redis, SQL Server).</para>
/// <para><strong>Limitation:</strong> <see cref="ListTasksAsync"/> returns an empty result
/// because key-value caches cannot query or filter across entries.
/// Use a database-backed <see cref="ITaskStore"/> implementation for
/// <c>ListTasks</c> support.</para>
/// <para><strong>Concurrency:</strong> <see cref="UpdateStatusAsync"/> and
/// <see cref="AppendHistoryAsync"/> perform read-modify-write cycles without locking.
/// Concurrent updates to the same task may lose writes. For production use,
/// consider implementing optimistic concurrency (e.g., ETags) or external locking.</para>
/// </remarks>
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
    /// <remarks>
    /// <strong>Concurrency warning:</strong> This method performs a read-modify-write
    /// cycle without locking. Concurrent updates to the same task may lose writes.
    /// For production use, consider implementing optimistic concurrency
    /// (e.g., ETags) or external locking.
    /// </remarks>
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
    /// <remarks>
    /// <strong>Concurrency warning:</strong> This method performs a read-modify-write
    /// cycle without locking. Concurrent updates to the same task may lose writes.
    /// For production use, consider implementing optimistic concurrency
    /// (e.g., ETags) or external locking.
    /// </remarks>
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
    /// <remarks>
    /// Always returns an empty result. Distributed caches do not support
    /// listing or filtering operations. Implement a database-backed
    /// <see cref="ITaskStore"/> for full <c>ListTasks</c> support.
    /// </remarks>
    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        // Distributed cache does not support listing/querying — return empty
        return Task.FromResult(new ListTasksResponse { Tasks = [] });
    }

    private static string BuildTaskCacheKey(string taskId) => $"task:{taskId}";
}

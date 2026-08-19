using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace A2A;

/// <summary>
/// In-memory task store using <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// Suitable for development and testing.
/// </summary>
/// <remarks>
/// Tasks are keyed by <c>(owner, taskId)</c> so that tasks created under one owner/tenant
/// scope are not visible to other owners (tenant isolation). When no owner is supplied
/// (unauthenticated requests), the shared <see cref="RequestContext.DefaultOwner"/> scope
/// is used, which preserves the previous flat-store behavior.
/// </remarks>
public sealed class InMemoryTaskStore : ITaskStore
{
    private readonly ConcurrentDictionary<(string Owner, string TaskId), AgentTask> _tasks = new();

    /// <inheritdoc />
    public Task<AgentTask?> GetTaskAsync(string taskId, string? owner = null, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue((GetEffectiveOwner(owner), taskId), out var task))
            return Task.FromResult<AgentTask?>(null);
        return Task.FromResult<AgentTask?>(CloneTask(task));
    }

    /// <inheritdoc />
    public Task SaveTaskAsync(string taskId, AgentTask task, string? owner = null, CancellationToken cancellationToken = default)
    {
        _tasks[(GetEffectiveOwner(owner), taskId)] = CloneTask(task);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteTaskAsync(string taskId, string? owner = null, CancellationToken cancellationToken = default)
    {
        _tasks.TryRemove((GetEffectiveOwner(owner), taskId), out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request,
        string? owner = null, CancellationToken cancellationToken = default)
    {
        var effectiveOwner = GetEffectiveOwner(owner);
        IEnumerable<AgentTask> allTasks = _tasks
            .Where(kvp => kvp.Key.Owner == effectiveOwner)
            .Select(kvp => kvp.Value)
            .Select(CloneTask);

        // Apply filters
        if (!string.IsNullOrEmpty(request.ContextId))
            allTasks = allTasks.Where(t => t.ContextId == request.ContextId);

        if (request.Status is { } statusFilter)
            allTasks = allTasks.Where(t => t.Status.State == statusFilter);

        if (request.StatusTimestampAfter is not null)
            allTasks = allTasks.Where(t =>
                t.Status.Timestamp is not null &&
                t.Status.Timestamp > request.StatusTimestampAfter);

        // Sort descending by status timestamp (newest first)
        var taskList = allTasks
            .OrderByDescending(t => t.Status.Timestamp ?? DateTimeOffset.MinValue)
            .ToList();

        var totalSize = taskList.Count;

        // Pagination
        int startIndex = 0;
        if (!string.IsNullOrEmpty(request.PageToken))
        {
            if (!int.TryParse(request.PageToken, out var offset) || offset < 0)
            {
                throw new A2AException(
                    $"Invalid pageToken: {request.PageToken}",
                    A2AErrorCode.InvalidParams);
            }

            startIndex = offset;
        }

        var pageSize = request.PageSize ?? 50;
        var page = taskList.Skip(startIndex).Take(pageSize).ToList();

        // Trim history
        if (request.HistoryLength is { } historyLength)
        {
            foreach (var task in page)
            {
                if (historyLength == 0)
                {
                    task.History = null;
                }
                else if (task.History is { Count: > 0 })
                {
                    task.History = task.History
                        .Skip(Math.Max(0, task.History.Count - historyLength))
                        .ToList();
                }
            }
        }

        if (request.IncludeArtifacts is not true)
        {
            foreach (var task in page)
            {
                task.Artifacts = null;
            }
        }

        var nextIndex = startIndex + page.Count;
        var nextPageToken = nextIndex < totalSize
            ? nextIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

        return Task.FromResult(new ListTasksResponse
        {
            Tasks = page,
            NextPageToken = nextPageToken,
            PageSize = page.Count,
            TotalSize = totalSize,
        });
    }

    /// <summary>
    /// Normalizes the caller-supplied owner: <c>null</c> or empty maps to the shared
    /// default owner scope so unauthenticated flows keep the flat-store behavior.
    /// </summary>
    /// <param name="owner">The caller-supplied owner scope, possibly <c>null</c>.</param>
    /// <returns>The effective owner scope.</returns>
    private static string GetEffectiveOwner(string? owner)
        => string.IsNullOrEmpty(owner) ? RequestContext.DefaultOwner : owner;

    [UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode", Justification = "All types are registered in source-generated JsonContext.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "All types are registered in source-generated JsonContext.")]
    private static AgentTask CloneTask(AgentTask task)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(task, A2AJsonUtilities.DefaultOptions);
        return JsonSerializer.Deserialize<AgentTask>(json, A2AJsonUtilities.DefaultOptions)!;
    }
}

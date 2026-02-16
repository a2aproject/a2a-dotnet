using System.Collections.Concurrent;

namespace A2A;

/// <summary>In-memory implementation of <see cref="ITaskStore"/>.</summary>
public sealed class InMemoryTaskStore : ITaskStore
{
    private readonly ConcurrentDictionary<string, AgentTask> _tasks = new();

    /// <inheritdoc />
    public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        _tasks.TryGetValue(taskId, out var task);
        return Task.FromResult<AgentTask?>(task);
    }

    /// <inheritdoc />
    public Task<AgentTask> SetTaskAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        _tasks[task.Id] = task;
        return Task.FromResult(task);
    }

    /// <inheritdoc />
    public Task<AgentTask> UpdateStatusAsync(string taskId, TaskStatus status, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out _))
        {
            throw new A2AException($"Task '{taskId}' not found.", A2AErrorCode.TaskNotFound);
        }

        var updated = _tasks.AddOrUpdate(
            taskId,
            _ => throw new A2AException($"Task '{taskId}' not found.", A2AErrorCode.TaskNotFound),
            (_, existing) =>
            {
                existing.Status = status;
                return existing;
            });

        return Task.FromResult(updated);
    }

    /// <inheritdoc />
    public Task<AgentTask> AppendHistoryAsync(string taskId, Message message, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out _))
        {
            throw new A2AException($"Task '{taskId}' not found.", A2AErrorCode.TaskNotFound);
        }

        var updated = _tasks.AddOrUpdate(
            taskId,
            _ => throw new A2AException($"Task '{taskId}' not found.", A2AErrorCode.TaskNotFound),
            (_, existing) =>
            {
                (existing.History ??= []).Add(message);
                return existing;
            });

        return Task.FromResult(updated);
    }

    /// <inheritdoc />
    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        IEnumerable<AgentTask> tasks = _tasks.Values;

        // Filtering
        if (request.ContextId is { } contextId)
        {
            tasks = tasks.Where(t => t.ContextId == contextId);
        }

        if (request.Status is { } status)
        {
            tasks = tasks.Where(t => t.Status.State == status);
        }

        if (request.StatusTimestampAfter is { } after)
        {
            tasks = tasks.Where(t => t.Status.Timestamp is { } ts && ts > after);
        }

        // Sort descending by status timestamp (newest first)
        var allFiltered = tasks
            .OrderByDescending(t => t.Status.Timestamp ?? DateTimeOffset.MinValue)
            .ToList();

        var totalSize = allFiltered.Count;

        // Pagination
        var pageSize = request.PageSize ?? 50;
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

        var page = allFiltered.Skip(startIndex).Take(pageSize).ToList();
        var nextIndex = startIndex + page.Count;
        var nextPageToken = nextIndex < totalSize ? nextIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

        // Apply historyLength truncation
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

        // Apply includeArtifacts filter (default: exclude artifacts)
        if (request.IncludeArtifacts is not true)
        {
            foreach (var task in page)
            {
                task.Artifacts = null;
            }
        }

        return Task.FromResult(new ListTasksResponse
        {
            Tasks = page,
            TotalSize = totalSize,
            PageSize = page.Count,
            NextPageToken = nextPageToken,
        });
    }
}
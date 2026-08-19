using A2A;
using System.Collections.Concurrent;
using System.Text.Json;

namespace AgentServer;

/// <summary>
/// File-backed task store with per-task JSON files and context index files
/// for efficient <see cref="ITaskStore.ListTasksAsync"/>.
/// </summary>
/// <remarks>
/// <para><strong>This is a sample implementation for demonstration purposes only.
/// Do not use in production.</strong> It lacks proper concurrency control across
/// processes, has no atomic index updates, and performs full file scans for
/// unindexed queries.</para>
/// <para>
/// Tasks are isolated per owner (tenant): each owner has its own directory so that
/// tasks created under one owner are not visible to another. The shared default owner
/// scope (<see cref="RequestContext.DefaultOwner"/>) is stored under the "default"
/// directory, preserving unauthenticated behavior.
/// Storage layout:
/// <code>
/// {baseDir}/
///   tasks/{owner}/{taskId}.json      — materialized AgentTask snapshot
///   indexes/{owner}/context_{id}.idx — newline-delimited task IDs per context
/// </code>
/// </para>
/// </remarks>
public sealed class FileTaskStore : ITaskStore
{
    private readonly string _tasksDir;
    private readonly string _indexesDir;

    // Compact JSON for storage
    private static readonly JsonSerializerOptions s_storageOptions = new(A2AJsonUtilities.DefaultOptions)
    {
        WriteIndented = false,
    };

    // Per-task lock to serialize writes (prevents interleaved writes to the same file)
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _taskLocks = new();

    public FileTaskStore(string baseDir)
    {
        _tasksDir = Path.Combine(baseDir, "tasks");
        _indexesDir = Path.Combine(baseDir, "indexes");

        Directory.CreateDirectory(_tasksDir);
        Directory.CreateDirectory(_indexesDir);
    }

    /// <inheritdoc />
    public async Task<AgentTask?> GetTaskAsync(string taskId, string? owner = null, CancellationToken cancellationToken = default)
    {
        return await ReadTaskAsync(taskId, GetEffectiveOwner(owner), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveTaskAsync(string taskId, AgentTask task, string? owner = null, CancellationToken cancellationToken = default)
    {
        var effectiveOwner = GetEffectiveOwner(owner);
        var taskLock = _taskLocks.GetOrAdd($"{effectiveOwner}/{taskId}", _ => new SemaphoreSlim(1, 1));
        await taskLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await WriteTaskAsync(taskId, effectiveOwner, task, cancellationToken).ConfigureAwait(false);

            // Update context index on save (idempotent)
            if (!string.IsNullOrEmpty(task.ContextId))
            {
                await AppendToIndexAsync("context", task.ContextId, taskId, effectiveOwner, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            taskLock.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>Not implemented in this sample. Throws <see cref="NotSupportedException"/>.</remarks>
    public Task DeleteTaskAsync(string taskId, string? owner = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Task deletion is not supported by this sample store.");

    /// <inheritdoc />
    public async Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request,
        string? owner = null, CancellationToken cancellationToken = default)
    {
        var effectiveOwner = GetEffectiveOwner(owner);

        // Step 1: Find candidate task IDs from index files
        var taskIds = await GetTaskIdsForQueryAsync(request, effectiveOwner, cancellationToken).ConfigureAwait(false);

        // Step 2: Load tasks for candidates
        var tasks = new List<AgentTask>();
        foreach (var taskId in taskIds)
        {
            var task = await ReadTaskAsync(taskId, effectiveOwner, cancellationToken).ConfigureAwait(false);
            if (task is null) continue;

            // Apply filters that aren't covered by the index
            if (request.Status is { } statusFilter && task.Status.State != statusFilter)
                continue;
            if (request.StatusTimestampAfter is not null &&
                (task.Status.Timestamp is null || task.Status.Timestamp <= request.StatusTimestampAfter))
                continue;

            tasks.Add(task);
        }

        // Step 3: Sort, paginate, trim
        tasks.Sort((a, b) =>
            (b.Status.Timestamp ?? DateTimeOffset.MinValue)
            .CompareTo(a.Status.Timestamp ?? DateTimeOffset.MinValue));

        var totalSize = tasks.Count;

        int startIndex = 0;
        if (!string.IsNullOrEmpty(request.PageToken))
        {
            if (!int.TryParse(request.PageToken, out var offset) || offset < 0)
                throw new A2AException($"Invalid pageToken: {request.PageToken}", A2AErrorCode.InvalidParams);
            startIndex = offset;
        }

        var pageSize = request.PageSize ?? 50;
        var page = tasks.Skip(startIndex).Take(pageSize).ToList();

        // Trim history/artifacts per request
        foreach (var task in page)
        {
            if (request.HistoryLength is { } historyLength)
            {
                if (historyLength == 0)
                    task.History = null;
                else if (task.History is { Count: > 0 })
                    task.History = task.History.TakeLast(historyLength).ToList();
            }

            if (request.IncludeArtifacts is not true)
                task.Artifacts = null;
        }

        var nextIndex = startIndex + page.Count;
        return new ListTasksResponse
        {
            Tasks = page,
            NextPageToken = nextIndex < totalSize
                ? nextIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
            PageSize = page.Count,
            TotalSize = totalSize,
        };
    }

    // ─── File I/O Helpers ───

    private static string GetEffectiveOwner(string? owner)
        => string.IsNullOrEmpty(owner) ? "default" : owner;

    private static string SanitizeOwner(string owner)
        => owner.Replace('\\', '_').Replace('/', '_');

    private string GetTaskFilePath(string taskId, string owner)
        => Path.Combine(_tasksDir, SanitizeOwner(owner), $"{taskId}.json");

    private string GetIndexFilePath(string prefix, string id, string owner)
        => Path.Combine(_indexesDir, SanitizeOwner(owner), $"{prefix}_{id}.idx");

    private string GetOwnerTasksDir(string owner) => Path.Combine(_tasksDir, SanitizeOwner(owner));
    private string GetOwnerIndexesDir(string owner) => Path.Combine(_indexesDir, SanitizeOwner(owner));

    private async Task<AgentTask?> ReadTaskAsync(string taskId, string owner, CancellationToken ct)
    {
        var path = GetTaskFilePath(taskId, owner);
        if (!File.Exists(path)) return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AgentTask>(stream, A2AJsonUtilities.DefaultOptions, ct)
            .ConfigureAwait(false);
    }

    private async Task WriteTaskAsync(string taskId, string owner, AgentTask task, CancellationToken ct)
    {
        var dir = GetOwnerTasksDir(owner);
        Directory.CreateDirectory(dir);
        var path = GetTaskFilePath(taskId, owner);
        var tempPath = path + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, task, A2AJsonUtilities.DefaultOptions, ct)
                .ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private async Task AppendToIndexAsync(string prefix, string id, string taskId, string owner, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(id)) return;

        var indexPath = GetIndexFilePath(prefix, id, owner);

        // Check if task is already in the index (avoid duplicates)
        if (File.Exists(indexPath))
        {
            var existing = await File.ReadAllTextAsync(indexPath, ct).ConfigureAwait(false);
            if (existing.Contains(taskId, StringComparison.Ordinal))
                return;
        }

        Directory.CreateDirectory(GetOwnerIndexesDir(owner));
        await File.AppendAllTextAsync(indexPath, taskId + Environment.NewLine, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> GetTaskIdsForQueryAsync(
        ListTasksRequest request, string owner, CancellationToken ct)
    {
        // Use the most specific index available
        if (!string.IsNullOrEmpty(request.ContextId))
            return await ReadIndexAsync("context", request.ContextId, owner, ct).ConfigureAwait(false);

        // No index match — scan all task files for this owner
        var dir = GetOwnerTasksDir(owner);
        if (!Directory.Exists(dir))
            return [];

        return Directory.GetFiles(dir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Cast<string>()
            .ToList();
    }

    private async Task<IReadOnlyList<string>> ReadIndexAsync(string prefix, string id, string owner, CancellationToken ct)
    {
        var path = GetIndexFilePath(prefix, id, owner);
        if (!File.Exists(path)) return [];

        var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }
}

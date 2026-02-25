using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace A2A;

/// <summary>
/// In-memory event store with inline projection cache.
/// Thread-safe via per-task locking.
/// </summary>
public sealed class InMemoryEventStore : ITaskEventStore
{
    private readonly ConcurrentDictionary<string, TaskEventLog> _logs = new();
    private readonly ChannelEventNotifier _notifier;

    /// <summary>Initializes a new instance of the <see cref="InMemoryEventStore"/> class.</summary>
    /// <param name="notifier">The notification bus for event fan-out.</param>
    public InMemoryEventStore(ChannelEventNotifier notifier)
    {
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
    }

    /// <inheritdoc />
    public Task<long> AppendAsync(string taskId, StreamResponse streamEvent,
        long? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        var log = _logs.GetOrAdd(taskId, _ => new TaskEventLog());
        var (version, envelope) = log.Append(streamEvent, expectedVersion);
        _notifier.Notify(taskId, envelope);
        return Task.FromResult(version);
    }

    /// <inheritdoc />
#pragma warning disable CS1998 // Async method lacks 'await' — synchronous in-memory read yielded as IAsyncEnumerable
    public async IAsyncEnumerable<EventEnvelope> ReadAsync(string taskId,
        long fromVersion = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_logs.TryGetValue(taskId, out var log))
            yield break;

        foreach (var envelope in log.Read(fromVersion))
        {
            yield return envelope;
        }
    }
#pragma warning restore CS1998

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string taskId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_logs.TryGetValue(taskId, out var log) && log.Count > 0);
    }

    /// <inheritdoc />
    public Task<long> GetLatestVersionAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!_logs.TryGetValue(taskId, out var log))
            return Task.FromResult(-1L);

        return Task.FromResult((long)(log.Count - 1));
    }

    /// <inheritdoc />
    public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!_logs.TryGetValue(taskId, out var log))
            return Task.FromResult<AgentTask?>(null);

        return Task.FromResult(log.GetProjection());
    }

    /// <inheritdoc />
    public Task<(AgentTask? Task, long Version)> GetTaskWithVersionAsync(
        string taskId, CancellationToken cancellationToken = default)
    {
        if (!_logs.TryGetValue(taskId, out var log))
            return Task.FromResult<(AgentTask?, long)>((null, -1));

        return Task.FromResult(log.GetProjectionWithVersion());
    }

    /// <inheritdoc />
    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<AgentTask> allTasks = _logs.Values
            .Select(log => log.GetProjection())
            .Where(t => t is not null)
            .Cast<AgentTask>();

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

        // Apply historyLength to the cloned objects
        // (GetProjection already deep-clones, so no mutation of stored state)
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

    // ─── Nested TaskEventLog ───

    private sealed class TaskEventLog
    {
        private readonly List<StreamResponse> _events = [];
        private readonly object _lock = new();
        private AgentTask? _projection;

        public int Count { get { lock (_lock) { return _events.Count; } } }

        public (long Version, EventEnvelope Envelope) Append(StreamResponse streamEvent, long? expectedVersion)
        {
            long version;
            EventEnvelope envelope;

            lock (_lock)
            {
                if (expectedVersion.HasValue && expectedVersion.Value != _events.Count)
                    throw new A2AException(
                        $"Concurrency conflict: expected version {expectedVersion.Value} but log is at {_events.Count}.",
                        A2AErrorCode.InvalidRequest);

                version = _events.Count;
                _events.Add(streamEvent);
                envelope = new EventEnvelope(version, streamEvent);

                // Update inline projection — clone Task events to avoid shared references
                // between the event log and the projection cache (mutations to projection
                // must not alter stored events).
                var applied = TaskProjection.Apply(_projection, streamEvent);
                _projection = applied is not null && streamEvent.Task is not null
                    ? CloneTask(applied)
                    : applied;
            }

            return (version, envelope);
        }

        public AgentTask? GetProjection()
        {
            lock (_lock)
            {
                if (_projection is null) return null;
                return CloneTask(_projection);
            }
        }

        public (AgentTask? Task, long Version) GetProjectionWithVersion()
        {
            lock (_lock)
            {
                var version = (long)(_events.Count - 1);
                if (_projection is null) return (null, version);
                return (CloneTask(_projection), version);
            }
        }

        public List<EventEnvelope> Read(long fromVersion)
        {
            lock (_lock)
            {
                if (fromVersion >= _events.Count) return [];
                return _events
                    .Skip((int)fromVersion)
                    .Select((e, i) => new EventEnvelope(fromVersion + i, e))
                    .ToList();
            }
        }

        [UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode", Justification = "All types are registered in source-generated JsonContext.")]
        [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "All types are registered in source-generated JsonContext.")]
        private static AgentTask CloneTask(AgentTask task)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(task, A2AJsonUtilities.DefaultOptions);
            return JsonSerializer.Deserialize<AgentTask>(json, A2AJsonUtilities.DefaultOptions)!;
        }
    }
}

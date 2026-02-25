using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace A2A;

/// <summary>
/// In-memory event store with inline projection cache and subscriber fan-out.
/// Thread-safe via per-task locking.
/// </summary>
public sealed class InMemoryEventStore : ITaskEventStore
{
    private readonly ConcurrentDictionary<string, TaskEventLog> _logs = new();

    /// <inheritdoc />
    public Task<long> AppendAsync(string taskId, StreamResponse streamEvent,
        long? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        var log = _logs.GetOrAdd(taskId, _ => new TaskEventLog());
        var version = log.Append(streamEvent, expectedVersion);
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
    public IAsyncEnumerable<EventEnvelope> SubscribeAsync(string taskId,
        long afterVersion = -1, CancellationToken cancellationToken = default)
    {
        var log = _logs.GetOrAdd(taskId, _ => new TaskEventLog());
        return log.SubscribeAsync(afterVersion, cancellationToken);
    }

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
        private readonly List<Channel<EventEnvelope>> _subscribers = [];
        private readonly object _lock = new();
        private AgentTask? _projection;

        public int Count { get { lock (_lock) { return _events.Count; } } }

        public long Append(StreamResponse streamEvent, long? expectedVersion)
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

            // Notify subscribers outside the lock (TryWrite is non-blocking)
            NotifySubscribers(envelope, streamEvent);

            return version;
        }

        public AgentTask? GetProjection()
        {
            lock (_lock)
            {
                if (_projection is null) return null;
                return CloneTask(_projection);
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

        public async IAsyncEnumerable<EventEnvelope> SubscribeAsync(
            long afterVersion,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var channel = Channel.CreateUnbounded<EventEnvelope>(
                new UnboundedChannelOptions { SingleWriter = false, SingleReader = true });

            lock (_lock)
            {
                _subscribers.Add(channel);
            }

            try
            {
                // Catch-up: replay events after requested position
                var catchUp = Read(afterVersion + 1);
                foreach (var evt in catchUp)
                {
                    yield return evt;
                    afterVersion = evt.Version;

                    // If catch-up included a terminal event, stop immediately
                    if (IsTerminalEvent(evt.Event))
                        yield break;
                }

                // Live: tail subscriber channel, dedup by version
                await foreach (var envelope in channel.Reader.ReadAllAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (envelope.Version <= afterVersion) continue;
                    afterVersion = envelope.Version;
                    yield return envelope;
                }
            }
            finally
            {
                lock (_lock) { _subscribers.Remove(channel); }
            }
        }

        private void NotifySubscribers(EventEnvelope envelope, StreamResponse streamEvent)
        {
            List<Channel<EventEnvelope>> subs;
            lock (_lock) { subs = [.. _subscribers]; }

            foreach (var sub in subs)
                sub.Writer.TryWrite(envelope);

            // If terminal state, complete all subscriber channels
            if (IsTerminalEvent(streamEvent))
            {
                lock (_lock) { subs = [.. _subscribers]; }
                foreach (var sub in subs)
                    sub.Writer.TryComplete();
            }
        }

        private static bool IsTerminalEvent(StreamResponse streamEvent) =>
            streamEvent.StatusUpdate?.Status.State.IsTerminal() == true ||
            streamEvent.Task?.Status.State.IsTerminal() == true;

        [UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode", Justification = "All types are registered in source-generated JsonContext.")]
        [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "All types are registered in source-generated JsonContext.")]
        private static AgentTask CloneTask(AgentTask task)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(task, A2AJsonUtilities.DefaultOptions);
            return JsonSerializer.Deserialize<AgentTask>(json, A2AJsonUtilities.DefaultOptions)!;
        }
    }
}

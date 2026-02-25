using A2A;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace AgentServer;

/// <summary>
/// File-backed event store with per-task event logs, materialized projection files,
/// and tenant/context index files for efficient <see cref="ITaskEventStore.ListTasksAsync"/>.
/// <para>
/// Storage layout:
/// <code>
/// {baseDir}/
///   events/{taskId}.jsonl         — append-only event log (one JSON line per event)
///   projections/{taskId}.json     — materialized AgentTask snapshot
///   indexes/tenant_{id}.idx       — newline-delimited task IDs per tenant
///   indexes/context_{id}.idx      — newline-delimited task IDs per context
/// </code>
/// </para>
/// <para>Subscribe uses in-memory channels for live event fan-out.
/// For distributed scenarios, replace with file watchers or a message broker.</para>
/// </summary>
public sealed class FileEventStore : ITaskEventStore
{
    private readonly string _baseDir;
    private readonly string _eventsDir;
    private readonly string _projectionsDir;
    private readonly string _indexesDir;

    // In-memory subscriber channels per task (live fan-out only — catch-up reads from disk)
    private readonly ConcurrentDictionary<string, SubscriberList> _subscribers = new();

    // Per-task lock to serialize appends (prevents interleaved writes to the same file)
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _taskLocks = new();

    public FileEventStore(string baseDir)
    {
        _baseDir = baseDir;
        _eventsDir = Path.Combine(baseDir, "events");
        _projectionsDir = Path.Combine(baseDir, "projections");
        _indexesDir = Path.Combine(baseDir, "indexes");

        Directory.CreateDirectory(_eventsDir);
        Directory.CreateDirectory(_projectionsDir);
        Directory.CreateDirectory(_indexesDir);
    }

    /// <inheritdoc />
    public async Task<long> AppendAsync(string taskId, StreamResponse streamEvent,
        long? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        var taskLock = _taskLocks.GetOrAdd(taskId, _ => new SemaphoreSlim(1, 1));
        await taskLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var eventFile = GetEventFilePath(taskId);
            var currentVersion = File.Exists(eventFile)
                ? CountLines(eventFile) - 1
                : -1L;

            if (expectedVersion.HasValue && expectedVersion.Value != currentVersion + 1)
            {
                throw new A2AException(
                    $"Concurrency conflict: expected version {expectedVersion.Value} but log is at {currentVersion + 1}.",
                    A2AErrorCode.InvalidRequest);
            }

            var newVersion = currentVersion + 1;

            // 1. Append event to the event log file
            var eventJson = JsonSerializer.Serialize(streamEvent, A2AJsonUtilities.DefaultOptions);
            await File.AppendAllTextAsync(eventFile, eventJson + Environment.NewLine, cancellationToken)
                .ConfigureAwait(false);

            // 2. Update materialized projection
            var projection = await ReadProjectionAsync(taskId, cancellationToken).ConfigureAwait(false);
            projection = TaskProjection.Apply(projection, streamEvent);
            await WriteProjectionAsync(taskId, projection, cancellationToken).ConfigureAwait(false);

            // 3. Update context index (only on initial Task event which carries contextId)
            if (streamEvent.Task is { } task && !string.IsNullOrEmpty(task.ContextId))
            {
                await AppendToIndexAsync("context", task.ContextId, taskId, cancellationToken)
                    .ConfigureAwait(false);
            }

            // 4. Notify live subscribers
            var envelope = new EventEnvelope(newVersion, streamEvent);
            NotifySubscribers(taskId, envelope, streamEvent);

            return newVersion;
        }
        finally
        {
            taskLock.Release();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<EventEnvelope> ReadAsync(string taskId,
        long fromVersion = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var eventFile = GetEventFilePath(taskId);
        if (!File.Exists(eventFile))
            yield break;

        long version = 0;
        await foreach (var line in ReadLinesAsync(eventFile, cancellationToken).ConfigureAwait(false))
        {
            if (version >= fromVersion)
            {
                var streamEvent = JsonSerializer.Deserialize<StreamResponse>(line, A2AJsonUtilities.DefaultOptions)!;
                yield return new EventEnvelope(version, streamEvent);
            }
            version++;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<EventEnvelope> SubscribeAsync(string taskId,
        long afterVersion = -1,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<EventEnvelope>(
            new UnboundedChannelOptions { SingleWriter = false, SingleReader = true });

        var subs = _subscribers.GetOrAdd(taskId, _ => new SubscriberList());
        lock (subs)
        {
            subs.Channels.Add(channel);
        }

        try
        {
            // Catch-up: read events from disk after the requested position
            await foreach (var evt in ReadAsync(taskId, afterVersion + 1, cancellationToken).ConfigureAwait(false))
            {
                yield return evt;
                afterVersion = evt.Version;

                if (IsTerminalEvent(evt.Event))
                    yield break;
            }

            // Live: tail subscriber channel, dedup by version
            await foreach (var envelope in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (envelope.Version <= afterVersion) continue;
                afterVersion = envelope.Version;
                yield return envelope;
            }
        }
        finally
        {
            lock (subs) { subs.Channels.Remove(channel); }
        }
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string taskId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(File.Exists(GetEventFilePath(taskId)));
    }

    /// <inheritdoc />
    public Task<long> GetLatestVersionAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var eventFile = GetEventFilePath(taskId);
        if (!File.Exists(eventFile))
            return Task.FromResult(-1L);

        return Task.FromResult(CountLines(eventFile) - 1);
    }

    /// <inheritdoc />
    public async Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        // Read directly from the materialized projection file — O(1), no event replay
        return await ReadProjectionAsync(taskId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Find candidate task IDs from index files
        var taskIds = await GetTaskIdsForQueryAsync(request, cancellationToken).ConfigureAwait(false);

        // Step 2: Load projections for candidate tasks
        var tasks = new List<AgentTask>();
        foreach (var taskId in taskIds)
        {
            var task = await ReadProjectionAsync(taskId, cancellationToken).ConfigureAwait(false);
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

    private string GetEventFilePath(string taskId) => Path.Combine(_eventsDir, $"{taskId}.jsonl");
    private string GetProjectionFilePath(string taskId) => Path.Combine(_projectionsDir, $"{taskId}.json");
    private string GetIndexFilePath(string prefix, string id) => Path.Combine(_indexesDir, $"{prefix}_{id}.idx");

    private async Task<AgentTask?> ReadProjectionAsync(string taskId, CancellationToken ct)
    {
        var path = GetProjectionFilePath(taskId);
        if (!File.Exists(path)) return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AgentTask>(stream, A2AJsonUtilities.DefaultOptions, ct)
            .ConfigureAwait(false);
    }

    private async Task WriteProjectionAsync(string taskId, AgentTask? projection, CancellationToken ct)
    {
        if (projection is null) return;

        var path = GetProjectionFilePath(taskId);
        var tempPath = path + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, projection, A2AJsonUtilities.DefaultOptions, ct)
                .ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private async Task AppendToIndexAsync(string prefix, string id, string taskId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(id)) return;

        var indexPath = GetIndexFilePath(prefix, id);

        // Check if task is already in the index (avoid duplicates)
        if (File.Exists(indexPath))
        {
            var existing = await File.ReadAllTextAsync(indexPath, ct).ConfigureAwait(false);
            if (existing.Contains(taskId, StringComparison.Ordinal))
                return;
        }

        await File.AppendAllTextAsync(indexPath, taskId + Environment.NewLine, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> GetTaskIdsForQueryAsync(
        ListTasksRequest request, CancellationToken ct)
    {
        // Use the most specific index available
        if (!string.IsNullOrEmpty(request.ContextId))
            return await ReadIndexAsync("context", request.ContextId, ct).ConfigureAwait(false);

        // No tenant index (AgentTask doesn't carry tenant) — scan all projections
        // Production: partition by tenant directory (e.g., events/{tenantId}/{taskId}.jsonl)
        return Directory.GetFiles(_projectionsDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Cast<string>()
            .ToList();
    }

    private async Task<IReadOnlyList<string>> ReadIndexAsync(string prefix, string id, CancellationToken ct)
    {
        var path = GetIndexFilePath(prefix, id);
        if (!File.Exists(path)) return [];

        var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private static long CountLines(string filePath)
    {
        long count = 0;
        using var reader = File.OpenText(filePath);
        while (reader.ReadLine() is not null)
            count++;
        return count;
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(string filePath,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(filePath);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                yield return line;
        }
    }

    // ─── Subscriber Management ───

    private void NotifySubscribers(string taskId, EventEnvelope envelope, StreamResponse streamEvent)
    {
        if (!_subscribers.TryGetValue(taskId, out var subs)) return;

        List<Channel<EventEnvelope>> channels;
        lock (subs) { channels = [.. subs.Channels]; }

        foreach (var ch in channels)
            ch.Writer.TryWrite(envelope);

        if (IsTerminalEvent(streamEvent))
        {
            lock (subs) { channels = [.. subs.Channels]; }
            foreach (var ch in channels)
                ch.Writer.TryComplete();
        }
    }

    private static bool IsTerminalEvent(StreamResponse streamEvent)
    {
        var state = streamEvent.StatusUpdate?.Status.State ?? streamEvent.Task?.Status.State;
        return state?.IsTerminal() == true;
    }

    private sealed class SubscriberList
    {
        public List<Channel<EventEnvelope>> Channels { get; } = [];
    }
}

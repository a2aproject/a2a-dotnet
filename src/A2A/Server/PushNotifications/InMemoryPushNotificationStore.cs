using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace A2A;

/// <summary>Concurrent development/test storage. Not durable, encrypted, or tenant-authorizing.</summary>
public sealed class InMemoryPushNotificationStore : IPushNotificationStore
{
    private readonly ConcurrentDictionary<string, Bucket> _tasks = new(StringComparer.Ordinal);
    private readonly byte[] _cursorKey = RandomNumberGenerator.GetBytes(32);

    /// <inheritdoc />
    public Task<PushNotificationConfigSnapshot> SaveAsync(TaskPushNotificationConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        cancellationToken.ThrowIfCancellationRequested();
        var copy = new PushNotificationConfigSnapshot(config, Guid.NewGuid().ToString("N")).Configuration;
        var bucket = GetBucket(copy.TaskId!, copy.Id, requireConfigId: true);
        lock (bucket)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bucket.Configs.ContainsKey(copy.Id!))
            {
                throw new A2AException("Push configuration ID already exists.", A2AErrorCode.InvalidParams);
            }
            var removed = new CancellationTokenSource();
            var snapshot = new PushNotificationConfigSnapshot(copy, Guid.NewGuid().ToString("N"), removed.Token);
            bucket.Configs.Add(copy.Id!, new Entry(snapshot, removed));
            bucket.Revision = Guid.NewGuid().ToString("N");
            return Task.FromResult(snapshot);
        }
    }

    /// <inheritdoc />
    public Task<PushNotificationConfigSnapshot?> GetAsync(string taskId, string configId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bucket = GetBucket(taskId, configId, requireConfigId: true);
        lock (bucket)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(bucket.Configs.GetValueOrDefault(configId)?.Snapshot);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PushNotificationConfigSnapshot>> GetAllAsync(string taskId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bucket = GetBucket(taskId);
        lock (bucket)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<PushNotificationConfigSnapshot>>(
                bucket.Configs.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value.Snapshot).ToArray());
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string taskId, string configId, string? version = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bucket = GetBucket(taskId, configId, requireConfigId: true);
        Entry? removed = null;
        lock (bucket)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bucket.Configs.TryGetValue(configId, out var current) &&
                (version is null || version == current.Snapshot.Version))
            {
                bucket.Configs.Remove(configId);
                bucket.Revision = Guid.NewGuid().ToString("N");
                removed = current;
            }
        }
        if (removed is not null)
        {
            try
            {
                await removed.Removed.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                removed.Removed.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public Task<ListTaskPushNotificationConfigsResponse> ListAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        int pageSize = request.PageSize.GetValueOrDefault(50);
        if (pageSize < 0)
        {
            throw new A2AException("Page size must be non-negative.", A2AErrorCode.InvalidParams);
        }
        pageSize = pageSize == 0 ? 50 : pageSize;
        pageSize = Math.Min(pageSize, 100);
        var bucket = GetBucket(request.TaskId);
        lock (bucket)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int offset = 0;
            if (!string.IsNullOrEmpty(request.PageToken))
            {
                offset = ReadCursor(request.PageToken, request.TaskId, bucket.Revision);
                if (offset >= bucket.Configs.Count)
                {
                    throw new A2AException("Invalid or stale push configuration cursor.", A2AErrorCode.InvalidParams);
                }
            }
            var configs = bucket.Configs.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Skip(offset).Take(pageSize).Select(pair => pair.Value.Snapshot.Configuration).ToList();
            int next = offset + configs.Count;
            return Task.FromResult(new ListTaskPushNotificationConfigsResponse
            {
                Configs = configs,
                NextPageToken = next < bucket.Configs.Count
                    ? WriteCursor(request.TaskId, bucket.Revision, next)
                    : string.Empty,
            });
        }
    }

    private Bucket GetBucket(string taskId, string? configId = null, bool requireConfigId = false)
    {
        if (string.IsNullOrWhiteSpace(taskId) ||
            taskId.Any(char.IsControl) ||
            (requireConfigId && string.IsNullOrWhiteSpace(configId)) ||
            (configId is not null && (string.IsNullOrWhiteSpace(configId) || configId.Any(char.IsControl))))
        {
            throw new A2AException("Invalid push configuration identifier.", A2AErrorCode.InvalidParams);
        }
        return _tasks.GetOrAdd(taskId, _ => new Bucket());
    }

    private string WriteCursor(string taskId, string revision, int offset)
    {
        var data = Encoding.UTF8.GetBytes($"{taskId}\n{revision}\n{offset.ToString(CultureInfo.InvariantCulture)}");
        return $"{Convert.ToBase64String(data)}.{Convert.ToBase64String(HMACSHA256.HashData(_cursorKey, data))}";
    }

    private int ReadCursor(string token, string taskId, string revision)
    {
        try
        {
            int separator = token.IndexOf('.');
            if (separator > 0 &&
                token.IndexOf('.', separator + 1) < 0)
            {
                var data = Convert.FromBase64String(token[..separator]);
                var signature = Convert.FromBase64String(token[(separator + 1)..]);
                var cursor = Encoding.UTF8.GetString(data);
                var prefix = $"{taskId}\n{revision}\n";
                bool valid = CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(_cursorKey, data)) &&
                    cursor.StartsWith(prefix, StringComparison.Ordinal);
                if (valid &&
                    int.TryParse(cursor.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int offset) &&
                    offset > 0)
                {
                    return offset;
                }
            }
        }
        catch (FormatException)
        {
            // Invalid base64 is a protocol parameter error, not a storage failure.
        }
        throw new A2AException("Invalid or stale push configuration cursor.", A2AErrorCode.InvalidParams);
    }

    private sealed class Bucket
    {
        internal Dictionary<string, Entry> Configs { get; } = new(StringComparer.Ordinal);
        internal string Revision { get; set; } = Guid.NewGuid().ToString("N");
    }

    private sealed record Entry(PushNotificationConfigSnapshot Snapshot, CancellationTokenSource Removed);
}

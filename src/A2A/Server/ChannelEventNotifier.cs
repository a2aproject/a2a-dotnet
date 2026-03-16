using System.Collections.Concurrent;
using System.Threading.Channels;

namespace A2A;

/// <summary>
/// Per-task subscriber channel management for event notification fan-out,
/// and per-task locking for atomic subscribe and persist operations.
/// </summary>
public sealed class ChannelEventNotifier
{
    private readonly ConcurrentDictionary<string, SubscriberSet> _subscribers = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _taskLocks = new();

    /// <summary>
    /// Push an event to all registered subscriber channels for the given task.
    /// On terminal events, completes all channels to end live tailing.
    /// Callers must hold the per-task lock when calling this method.
    /// </summary>
    /// <param name="taskId">The task to notify subscribers for.</param>
    /// <param name="streamEvent">The stream response event.</param>
    public void Notify(string taskId, StreamResponse streamEvent)
    {
        if (!_subscribers.TryGetValue(taskId, out var set)) return;

        List<Channel<StreamResponse>> channels;
        lock (set) { channels = [.. set.Channels]; }

        foreach (var ch in channels)
            ch.Writer.TryWrite(streamEvent);

        if (IsTerminalEvent(streamEvent))
        {
            lock (set) { channels = [.. set.Channels]; }
            foreach (var ch in channels)
                ch.Writer.TryComplete();
        }
    }

    /// <summary>Creates and registers a subscriber channel for the given task.</summary>
    /// <param name="taskId">The task to create a subscriber channel for.</param>
    internal Channel<StreamResponse> CreateChannel(string taskId)
    {
        var channel = Channel.CreateUnbounded<StreamResponse>(
            new UnboundedChannelOptions { SingleWriter = false, SingleReader = true });

        var set = _subscribers.GetOrAdd(taskId, _ => new SubscriberSet());
        lock (set) { set.Channels.Add(channel); }
        return channel;
    }

    /// <summary>Unregisters a channel when subscription ends.</summary>
    /// <param name="taskId">The task to remove the channel from.</param>
    /// <param name="channel">The channel to remove.</param>
    internal void RemoveChannel(string taskId, Channel<StreamResponse> channel)
    {
        if (!_subscribers.TryGetValue(taskId, out var set))
        {
            throw new InvalidOperationException(
                $"No subscriber set found for task '{taskId}'. " +
                "This indicates a bug: RemoveChannel was called without a matching CreateChannel, " +
                "or the subscriber set was evicted by a concurrent call.");
        }

        lock (set)
        {
            set.Channels.Remove(channel);
            if (set.Channels.Count == 0)
            {
                _subscribers.TryRemove(taskId, out _);
                _taskLocks.TryRemove(taskId, out _);
            }
        }
    }

    /// <summary>
    /// Acquire the per-task lock used to atomically read task state and register
    /// a subscriber channel, preventing race conditions with concurrent mutations.
    /// </summary>
    /// <param name="taskId">The task to acquire the lock for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task<IDisposable> AcquireTaskLockAsync(
        string taskId, CancellationToken cancellationToken = default)
    {
        // Retry loop handles the race where RemoveChannel evicts the
        // semaphore between GetOrAdd and WaitAsync completion.
        while (true)
        {
            var sem = _taskLocks.GetOrAdd(taskId, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync(cancellationToken).ConfigureAwait(false);

            // Verify this semaphore is still the live entry.
            if (_taskLocks.TryGetValue(taskId, out var current) && ReferenceEquals(current, sem))
                return new TaskLockRelease(sem);

            // Evicted while waiting — release the orphaned semaphore and retry.
            sem.Release();
        }
    }

    private static bool IsTerminalEvent(StreamResponse streamEvent)
    {
        var state = streamEvent.StatusUpdate?.Status.State ?? streamEvent.Task?.Status.State;
        return state?.IsTerminal() == true;
    }

    private sealed class TaskLockRelease(SemaphoreSlim semaphore) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                semaphore.Release();
        }
    }

    private sealed class SubscriberSet
    {
        public List<Channel<StreamResponse>> Channels { get; } = [];
    }
}

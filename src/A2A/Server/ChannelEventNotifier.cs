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
    private readonly ConcurrentDictionary<string, TaskLockState> _taskLocks = new();
    private readonly ConcurrentDictionary<string, TaskLockState> _executionLocks = new();

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

        while (true)
        {
            var set = _subscribers.GetOrAdd(taskId, _ => new SubscriberSet());
            lock (set)
            {
                if (set.Retired)
                {
                    continue;
                }
                set.Channels.Add(channel);
                return channel;
            }
        }
    }

    /// <summary>Unregisters a channel when subscription ends.</summary>
    /// <param name="taskId">The task to remove the channel from.</param>
    /// <param name="channel">The channel to remove.</param>
    internal void RemoveChannel(string taskId, Channel<StreamResponse> channel)
    {
        if (!_subscribers.TryGetValue(taskId, out var set))
        {
            return;
        }

        lock (set)
        {
            set.Channels.Remove(channel);
            if (set.Channels.Count == 0)
            {
                set.Retired = true;
                _subscribers.TryRemove(taskId, out _);
            }
        }
    }

    /// <summary>
    /// Acquire the per-task lock used to atomically read task state and register
    /// a subscriber channel, preventing race conditions with concurrent mutations.
    /// </summary>
    /// <param name="taskId">The task to acquire the lock for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IDisposable> AcquireTaskLockAsync(
        string taskId, CancellationToken cancellationToken = default) =>
        AcquireLockAsync(_taskLocks, taskId, cancellationToken);

    internal Task<IDisposable> AcquireTaskExecutionLockAsync(string taskId, CancellationToken cancellationToken) =>
        AcquireLockAsync(_executionLocks, taskId, cancellationToken);

    private static async Task<IDisposable> AcquireLockAsync(
        ConcurrentDictionary<string, TaskLockState> locks, string taskId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var state = locks.GetOrAdd(taskId, _ => new TaskLockState());
            lock (state)
            {
                if (state.Retired)
                {
                    continue;
                }
                state.References++;
            }
            try
            {
                await state.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new TaskLockRelease(locks, taskId, state);
            }
            catch
            {
                ReleaseTaskLockReference(locks, taskId, state);
                throw;
            }
        }
    }

    private static void ReleaseTaskLockReference(ConcurrentDictionary<string, TaskLockState> locks, string taskId, TaskLockState state)
    {
        lock (state)
        {
            state.References--;
            if (state.References == 0)
            {
                state.Retired = true;
                locks.TryRemove(taskId, out _);
                state.Semaphore.Dispose();
            }
        }
    }

    private static bool IsTerminalEvent(StreamResponse streamEvent)
    {
        var state = streamEvent.StatusUpdate?.Status.State ?? streamEvent.Task?.Status.State;
        return state?.IsTerminal() == true;
    }

    private sealed class TaskLockRelease(ConcurrentDictionary<string, TaskLockState> locks, string taskId, TaskLockState state) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                state.Semaphore.Release();
                ReleaseTaskLockReference(locks, taskId, state);
            }
        }
    }

    private sealed class TaskLockState
    {
        internal SemaphoreSlim Semaphore { get; } = new(1, 1);
        internal int References { get; set; }
        internal bool Retired { get; set; }
    }

    private sealed class SubscriberSet
    {
        public List<Channel<StreamResponse>> Channels { get; } = [];
        public bool Retired { get; set; }
    }
}

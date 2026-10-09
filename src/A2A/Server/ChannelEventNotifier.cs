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
    private readonly ConcurrentDictionary<string, TaskLock> _taskLocks = new();

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
            return;
        }

        lock (set)
        {
            set.Channels.Remove(channel);
            if (set.Channels.Count == 0)
            {
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
    public async Task<IDisposable> AcquireTaskLockAsync(
        string taskId, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var taskLock = _taskLocks.GetOrAdd(taskId, static _ => new TaskLock());
            if (!taskLock.TryAddReference())
            {
                continue;
            }

            try
            {
                await taskLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new TaskLockRelease(this, taskId, taskLock);
            }
            catch
            {
                ReleaseTaskLockReference(taskId, taskLock);
                throw;
            }
        }
    }

    private void ReleaseTaskLock(string taskId, TaskLock taskLock)
    {
        taskLock.Semaphore.Release();
        ReleaseTaskLockReference(taskId, taskLock);
    }

    private void ReleaseTaskLockReference(string taskId, TaskLock taskLock)
    {
        if (taskLock.ReleaseReference())
        {
            ((ICollection<KeyValuePair<string, TaskLock>>)_taskLocks)
                .Remove(new KeyValuePair<string, TaskLock>(taskId, taskLock));
        }
    }

    private static bool IsTerminalEvent(StreamResponse streamEvent)
    {
        var state = streamEvent.StatusUpdate?.Status.State ?? streamEvent.Task?.Status.State;
        return state?.IsTerminal() == true;
    }

    private sealed class TaskLockRelease(
        ChannelEventNotifier owner,
        string taskId,
        TaskLock taskLock) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.ReleaseTaskLock(taskId, taskLock);
            }
        }
    }

    private sealed class TaskLock
    {
        private int _referenceCount;
        private bool _retired;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryAddReference()
        {
            lock (this)
            {
                if (_retired)
                {
                    return false;
                }

                _referenceCount++;
                return true;
            }
        }

        public bool ReleaseReference()
        {
            lock (this)
            {
                _referenceCount--;
                if (_referenceCount == 0)
                {
                    _retired = true;
                    return true;
                }

                return false;
            }
        }
    }

    private sealed class SubscriberSet
    {
        public List<Channel<StreamResponse>> Channels { get; } = [];
    }
}

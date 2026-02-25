using System.Collections.Concurrent;
using System.Threading.Channels;

namespace A2A;

/// <summary>
/// Per-task subscriber channel management for event notification fan-out.
/// The event store calls <see cref="Notify"/> after persisting an event;
/// <see cref="ChannelEventSubscriber"/> reads from channels for live events.
/// </summary>
public sealed class ChannelEventNotifier
{
    private readonly ConcurrentDictionary<string, SubscriberSet> _subscribers = new();

    /// <summary>
    /// Push an event to all registered subscriber channels for the given task.
    /// On terminal events, completes all channels to end live tailing.
    /// Called by the event store after persisting an event.
    /// </summary>
    /// <param name="taskId">The task to notify subscribers for.</param>
    /// <param name="envelope">The event envelope containing the version and event.</param>
    public void Notify(string taskId, EventEnvelope envelope)
    {
        if (!_subscribers.TryGetValue(taskId, out var set)) return;

        List<Channel<EventEnvelope>> channels;
        lock (set) { channels = [.. set.Channels]; }

        foreach (var ch in channels)
            ch.Writer.TryWrite(envelope);

        if (IsTerminalEvent(envelope.Event))
        {
            lock (set) { channels = [.. set.Channels]; }
            foreach (var ch in channels)
                ch.Writer.TryComplete();
        }
    }

    /// <summary>Creates and registers a subscriber channel for the given task.</summary>
    /// <param name="taskId">The task to create a subscriber channel for.</param>
    internal Channel<EventEnvelope> CreateChannel(string taskId)
    {
        var channel = Channel.CreateUnbounded<EventEnvelope>(
            new UnboundedChannelOptions { SingleWriter = false, SingleReader = true });

        var set = _subscribers.GetOrAdd(taskId, _ => new SubscriberSet());
        lock (set) { set.Channels.Add(channel); }
        return channel;
    }

    /// <summary>Unregisters a channel when subscription ends.</summary>
    /// <param name="taskId">The task to remove the channel from.</param>
    /// <param name="channel">The channel to remove.</param>
    internal void RemoveChannel(string taskId, Channel<EventEnvelope> channel)
    {
        if (!_subscribers.TryGetValue(taskId, out var set)) return;
        lock (set) { set.Channels.Remove(channel); }
    }

    private static bool IsTerminalEvent(StreamResponse streamEvent)
    {
        var state = streamEvent.StatusUpdate?.Status.State ?? streamEvent.Task?.Status.State;
        return state?.IsTerminal() == true;
    }

    private sealed class SubscriberSet
    {
        public List<Channel<EventEnvelope>> Channels { get; } = [];
    }
}

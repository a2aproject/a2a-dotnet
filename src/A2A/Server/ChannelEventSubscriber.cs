using System.Runtime.CompilerServices;

namespace A2A;

/// <summary>
/// Default <see cref="IEventSubscriber"/> that uses catch-up-then-live pattern.
/// Reads historical events from <see cref="IEventStore.ReadAsync"/> and
/// live events from <see cref="ChannelEventNotifier"/> channels.
/// </summary>
/// <param name="store">The event store for catch-up reads.</param>
/// <param name="notifier">The notification bus for live event channels.</param>
public sealed class ChannelEventSubscriber(IEventStore store, ChannelEventNotifier notifier)
    : IEventSubscriber
{
    /// <inheritdoc />
    public async IAsyncEnumerable<EventEnvelope> SubscribeAsync(
        string taskId,
        long afterVersion = -1,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Register channel BEFORE catch-up to avoid missing events
        // appended between catch-up read and channel registration.
        var channel = notifier.CreateChannel(taskId);

        try
        {
            // Catch-up: read persisted events after the requested position
            await foreach (var evt in store.ReadAsync(taskId, afterVersion + 1, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return evt;
                afterVersion = evt.Version;

                if (IsTerminalEvent(evt.Event))
                    yield break;
            }

            // Live: tail the subscriber channel, dedup by version
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
            notifier.RemoveChannel(taskId, channel);
        }
    }

    private static bool IsTerminalEvent(StreamResponse streamEvent)
    {
        var state = streamEvent.StatusUpdate?.Status.State ?? streamEvent.Task?.Status.State;
        return state?.IsTerminal() == true;
    }
}

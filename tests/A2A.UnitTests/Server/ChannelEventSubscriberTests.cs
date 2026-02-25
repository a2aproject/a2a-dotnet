namespace A2A.UnitTests.Server;

public class ChannelEventSubscriberTests
{
    private static (ChannelEventSubscriber subscriber, InMemoryEventStore store, ChannelEventNotifier notifier) CreateSubscriber()
    {
        var notifier = new ChannelEventNotifier();
        var store = new InMemoryEventStore(notifier);
        var subscriber = new ChannelEventSubscriber(store, notifier);
        return (subscriber, store, notifier);
    }

    [Fact]
    public async Task SubscribeAsync_ShouldDeliverCatchUpEvents()
    {
        // Arrange — append events first, then subscribe
        var (subscriber, store, _) = CreateSubscriber();
        await store.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } }
        });
        await store.AppendAsync("t1", new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Completed },
            }
        });

        // Act — subscribe from version -1 (catch-up all events)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new List<EventEnvelope>();
        await foreach (var e in subscriber.SubscribeAsync("t1", afterVersion: -1, cts.Token))
        {
            events.Add(e);
        }

        // Assert — should see both events via catch-up, stream ends on terminal
        Assert.Equal(2, events.Count);
        Assert.Equal(0, events[0].Version);
        Assert.Equal(1, events[1].Version);
    }

    [Fact]
    public async Task SubscribeAsync_ShouldDeliverLiveEvents()
    {
        // Arrange — subscribe first, then append events
        var (subscriber, store, _) = CreateSubscriber();
        await store.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new List<EventEnvelope>();

        // Act — subscribe from version 0 (skip catch-up of initial event), then append
        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var e in subscriber.SubscribeAsync("t1", afterVersion: 0, cts.Token))
            {
                events.Add(e);
                break; // Only need the first live event
            }
        }, cts.Token);

        // Give subscriber time to register its channel
        await Task.Delay(100, cts.Token);

        await store.AppendAsync("t1", new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Completed },
            }
        });

        await subscribeTask;

        // Assert
        Assert.Single(events);
        Assert.Equal(1, events[0].Version);
        Assert.NotNull(events[0].Event.StatusUpdate);
        Assert.Equal(TaskState.Completed, events[0].Event.StatusUpdate!.Status.State);
    }

    [Fact]
    public async Task SubscribeAsync_ShouldCompleteOnTerminalState()
    {
        // Arrange
        var (subscriber, store, _) = CreateSubscriber();
        await store.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Working } }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new List<EventEnvelope>();

        // Act — subscribe, then push a terminal status
        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var e in subscriber.SubscribeAsync("t1", afterVersion: 0, cts.Token))
            {
                events.Add(e);
            }
        }, cts.Token);

        await Task.Delay(100, cts.Token);

        await store.AppendAsync("t1", new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Failed },
            }
        });

        // Subscription should complete promptly due to terminal event
        await subscribeTask;

        // Assert
        Assert.Single(events);
        Assert.Equal(TaskState.Failed, events[0].Event.StatusUpdate!.Status.State);
    }

    [Fact]
    public async Task SubscribeAsync_ShouldDeduplicateByVersion()
    {
        // Arrange — subscribe from version -1, events will arrive via both catch-up and live channel
        var (subscriber, store, _) = CreateSubscriber();
        await store.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Working } }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new List<EventEnvelope>();

        // Act — subscribe from -1 (catch-up reads version 0), then append terminal
        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var e in subscriber.SubscribeAsync("t1", afterVersion: -1, cts.Token))
            {
                events.Add(e);
            }
        }, cts.Token);

        await Task.Delay(100, cts.Token);

        await store.AppendAsync("t1", new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Completed },
            }
        });

        await subscribeTask;

        // Assert — version 0 from catch-up + version 1 from live, no duplicates
        Assert.Equal(2, events.Count);
        Assert.Equal(0, events[0].Version);
        Assert.Equal(1, events[1].Version);
    }

    [Fact]
    public async Task SubscribeAsync_ShouldNotMissEventsBetweenCatchUpAndLive()
    {
        // Arrange — register channel before catch-up ensures no gap
        var (subscriber, store, _) = CreateSubscriber();

        // Pre-populate with several events
        await store.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } }
        });
        await store.AppendAsync("t1", new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Working },
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new List<EventEnvelope>();

        // Act — subscribe from 0 (catch-up + live for version 1 and beyond)
        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var e in subscriber.SubscribeAsync("t1", afterVersion: 0, cts.Token))
            {
                events.Add(e);
            }
        }, cts.Token);

        await Task.Delay(100, cts.Token);

        // Append terminal event while subscriber is tailing
        await store.AppendAsync("t1", new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Completed },
            }
        });

        await subscribeTask;

        // Assert — version 1 from catch-up, version 2 from live
        Assert.Equal(2, events.Count);
        Assert.Equal(1, events[0].Version);
        Assert.Equal(2, events[1].Version);
    }
}

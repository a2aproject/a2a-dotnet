using System.Threading.Channels;

namespace A2A.UnitTests.Server;

public class ChannelEventNotifierTests
{
    private static StreamResponse WorkingStatus(string taskId = "t1")
        => new()
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = taskId,
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Working },
            }
        };

    private static StreamResponse TerminalStatus(string taskId = "t1")
        => new()
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = taskId,
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Completed },
            }
        };

    [Fact]
    public void Notify_WithNoSubscribers_DoesNotThrow()
    {
        // Arrange
        var notifier = new ChannelEventNotifier();
        var evt = WorkingStatus();

        // Act & Assert — should not throw
        notifier.Notify("t1", evt);
    }

    [Fact]
    public async Task Notify_PushesToAllRegisteredChannels()
    {
        // Arrange
        var notifier = new ChannelEventNotifier();
        var ch1 = notifier.CreateChannel("t1");
        var ch2 = notifier.CreateChannel("t1");

        var evt = WorkingStatus();

        // Act
        notifier.Notify("t1", evt);

        // Assert — both channels should receive the event
        Assert.True(ch1.Reader.TryRead(out var r1));
        Assert.NotNull(r1.StatusUpdate);

        Assert.True(ch2.Reader.TryRead(out var r2));
        Assert.NotNull(r2.StatusUpdate);
    }

    [Fact]
    public async Task Notify_TerminalEvent_CompletesChannels()
    {
        // Arrange
        var notifier = new ChannelEventNotifier();
        var ch = notifier.CreateChannel("t1");

        var evt = TerminalStatus();

        // Act
        notifier.Notify("t1", evt);

        // Assert — channel should receive the event and then complete
        Assert.True(ch.Reader.TryRead(out var received));
        Assert.NotNull(received.StatusUpdate);
        Assert.Equal(TaskState.Completed, received.StatusUpdate!.Status.State);

        // Reader should complete since the event is terminal
        await ch.Reader.Completion;
    }

    [Fact]
    public void CreateChannel_RegistersForTask()
    {
        // Arrange
        var notifier = new ChannelEventNotifier();

        // Act
        var ch = notifier.CreateChannel("t1");
        var evt = WorkingStatus();
        notifier.Notify("t1", evt);

        // Assert — channel should receive the event
        Assert.True(ch.Reader.TryRead(out var received));
        Assert.NotNull(received.StatusUpdate);
    }

    [Fact]
    public void RemoveChannel_UnregistersChannel()
    {
        // Arrange
        var notifier = new ChannelEventNotifier();
        var ch = notifier.CreateChannel("t1");

        // Act
        notifier.RemoveChannel("t1", ch);

        var evt = WorkingStatus();
        notifier.Notify("t1", evt);

        // Assert — removed channel should NOT receive the event
        Assert.False(ch.Reader.TryRead(out _));
    }

    [Fact]
    public void Notify_AfterRemoveChannel_DoesNotPushToRemoved()
    {
        // Arrange
        var notifier = new ChannelEventNotifier();
        var ch1 = notifier.CreateChannel("t1");
        var ch2 = notifier.CreateChannel("t1");

        // Remove ch1 but keep ch2
        notifier.RemoveChannel("t1", ch1);

        var evt = WorkingStatus();

        // Act
        notifier.Notify("t1", evt);

        // Assert — ch1 should NOT receive, ch2 should receive
        Assert.False(ch1.Reader.TryRead(out _));
        Assert.True(ch2.Reader.TryRead(out var received));
        Assert.NotNull(received.StatusUpdate);
    }

    [Fact]
    public async Task AcquireTaskLockAsync_BlocksConcurrentAccess()
    {
        // Arrange
        var notifier = new ChannelEventNotifier();
        var lockAcquired = false;
        var lockReleased = false;

        // Act — acquire lock, verify second acquire blocks until first released
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var firstLock = await notifier.AcquireTaskLockAsync("t1", cts.Token);

        var secondLockTask = Task.Run(async () =>
        {
            lockAcquired = true;
            using var secondLock = await notifier.AcquireTaskLockAsync("t1", cts.Token);
            lockReleased = true;
        }, cts.Token);

        // Allow time for the second lock attempt to start waiting
        await Task.Delay(200, cts.Token);

        // Assert — second lock should be waiting (lockReleased still false)
        Assert.True(lockAcquired);
        Assert.False(lockReleased);

        // Release first lock
        firstLock.Dispose();
        await secondLockTask;

        // Assert — second lock was acquired and released
        Assert.True(lockReleased);
    }

    [Fact]
    public async Task AcquireTaskLockAsync_IsPerTask_NotGlobal()
    {
        // Arrange
        var notifier = new ChannelEventNotifier();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act — acquire lock for t1, then immediately acquire for t2 (should not block)
        using var lock1 = await notifier.AcquireTaskLockAsync("t1", cts.Token);
        using var lock2 = await notifier.AcquireTaskLockAsync("t2", cts.Token);

        // Assert — if we get here, locks for different tasks don't block each other
        Assert.NotNull(lock1);
        Assert.NotNull(lock2);
    }

    [Fact]
    public void Notify_DoesNotCrossTaskBoundary()
    {
        var notifier = new ChannelEventNotifier();
        var chA = notifier.CreateChannel("task-a");
        var chB = notifier.CreateChannel("task-b");

        notifier.Notify("task-a", WorkingStatus("task-a"));

        Assert.True(chA.Reader.TryRead(out _));   // task-a gets it
        Assert.False(chB.Reader.TryRead(out _));  // task-b does not
    }

    [Fact]
    public async Task ConcurrentNotify_DoesNotCorruptChannelList()
    {
        var notifier = new ChannelEventNotifier();

        // Run 20 concurrent operations: create channels, notify, remove channels
        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            if (i % 3 == 0)
            {
                var ch = notifier.CreateChannel("t1");
                notifier.RemoveChannel("t1", ch);
            }
            else
            {
                notifier.Notify("t1", WorkingStatus());
            }
        }));

        await Task.WhenAll(tasks); // Should not throw or deadlock
    }
}

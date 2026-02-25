using System.Threading.Channels;

namespace A2A.UnitTests.Server;

public class ChannelEventNotifierTests
{
    private static EventEnvelope CreateEnvelope(long version, StreamResponse evt)
        => new() { Version = version, Event = evt };

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
        var envelope = CreateEnvelope(0, evt);

        // Act & Assert — should not throw
        notifier.Notify("t1", envelope);
    }

    [Fact]
    public async Task Notify_PushesToAllRegisteredChannels()
    {
        // Arrange
        var notifier = new ChannelEventNotifier();
        var ch1 = notifier.CreateChannel("t1");
        var ch2 = notifier.CreateChannel("t1");

        var evt = WorkingStatus();
        var envelope = CreateEnvelope(0, evt);

        // Act
        notifier.Notify("t1", envelope);

        // Assert — both channels should receive the event
        Assert.True(ch1.Reader.TryRead(out var r1));
        Assert.Equal(0, r1.Version);

        Assert.True(ch2.Reader.TryRead(out var r2));
        Assert.Equal(0, r2.Version);
    }

    [Fact]
    public async Task Notify_TerminalEvent_CompletesChannels()
    {
        // Arrange
        var notifier = new ChannelEventNotifier();
        var ch = notifier.CreateChannel("t1");

        var evt = TerminalStatus();
        var envelope = CreateEnvelope(1, evt);

        // Act
        notifier.Notify("t1", envelope);

        // Assert — channel should receive the event and then complete
        Assert.True(ch.Reader.TryRead(out var received));
        Assert.Equal(1, received.Version);

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
        var envelope = CreateEnvelope(0, evt);
        notifier.Notify("t1", envelope);

        // Assert — channel should receive the event
        Assert.True(ch.Reader.TryRead(out var received));
        Assert.Equal(0, received.Version);
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
        var envelope = CreateEnvelope(0, evt);
        notifier.Notify("t1", envelope);

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
        var envelope = CreateEnvelope(0, evt);

        // Act
        notifier.Notify("t1", envelope);

        // Assert — ch1 should NOT receive, ch2 should receive
        Assert.False(ch1.Reader.TryRead(out _));
        Assert.True(ch2.Reader.TryRead(out var received));
        Assert.Equal(0, received.Version);
    }
}

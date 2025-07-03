namespace A2A.UnitTests.Server;

public class TaskUpdateEventEnumeratorTests
{
    [Fact]
    public async Task NotifyEvent_ShouldYieldEvent()
    {
        // Arrange
        var enumerator = new TaskUpdateEventEnumerator();
        var evt = new TaskStatusUpdateEvent { TaskId = "t1", Status = new AgentTaskStatus { State = TaskState.Submitted } };
        enumerator.NotifyEvent(evt);

        // Act
        List<A2AEvent> yielded = [];
        await foreach (var e in enumerator.WithCancellation(new CancellationTokenSource(100).Token))
        {
            yielded.Add(e);
            break; // Only one event expected
        }

        // Assert
        Assert.Single(yielded);
        Assert.Equal(evt, yielded[0]);
    }

    [Fact]
    public async Task NotifyFinalEvent_ShouldYieldAndComplete()
    {
        // Arrange
        var enumerator = new TaskUpdateEventEnumerator();
        var evt = new TaskStatusUpdateEvent { TaskId = "t2", Status = new AgentTaskStatus { State = TaskState.Completed }, Final = true };
        enumerator.NotifyFinalEvent(evt);

        // Act
        List<A2AEvent> yielded = [];
        await foreach (var e in enumerator)
        {
            yielded.Add(e);
        }

        // Assert
        Assert.Single(yielded);
        Assert.Equal(evt, yielded[0]);
    }

    [Fact]
    public async Task MultipleEvents_ShouldYieldInOrder()
    {
        // Arrange
        var enumerator = new TaskUpdateEventEnumerator();
        var evt1 = new TaskStatusUpdateEvent { TaskId = "t3", Status = new AgentTaskStatus { State = TaskState.Submitted } };
        var evt2 = new TaskStatusUpdateEvent { TaskId = "t3", Status = new AgentTaskStatus { State = TaskState.Working } };
        var evt3 = new TaskStatusUpdateEvent { TaskId = "t3", Status = new AgentTaskStatus { State = TaskState.Completed }, Final = true };
        enumerator.NotifyEvent(evt1);
        enumerator.NotifyEvent(evt2);
        enumerator.NotifyFinalEvent(evt3);

        // Act
        List<A2AEvent> yielded = [];
        await foreach (var e in enumerator)
        {
            yielded.Add(e);
        }

        // Assert
        Assert.Equal(3, yielded.Count);
        Assert.Equal(evt1, yielded[0]);
        Assert.Equal(evt2, yielded[1]);
        Assert.Equal(evt3, yielded[2]);
    }

    [Fact]
    public async Task Enumerator_ShouldSupportCancellation()
    {
        // Arrange
        var enumerator = new TaskUpdateEventEnumerator();
        var cts = new CancellationTokenSource(50);
        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in enumerator.WithCancellation(cts.Token))
            {
                // Should not yield
            }
        });
    }
}

namespace A2A.UnitTests.Server;

public class AgentEventQueueTests
{
    [Fact]
    public async Task WriteAsync_ShouldYieldResponse()
    {
        // Arrange
        var queue = new AgentEventQueue();
        var response = new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent { TaskId = "t1", Status = new TaskStatus { State = TaskState.Submitted } }
        };
        await queue.WriteAsync(response);
        queue.Complete();

        // Act
        List<StreamResponse> yielded = [];
        await foreach (var e in queue)
        {
            yielded.Add(e);
        }

        // Assert
        Assert.Single(yielded);
        Assert.NotNull(yielded[0].StatusUpdate);
        Assert.Equal("t1", yielded[0].StatusUpdate!.TaskId);
    }

    [Fact]
    public async Task Complete_ShouldEndEnumeration()
    {
        // Arrange
        var queue = new AgentEventQueue();
        var r1 = new StreamResponse { StatusUpdate = new TaskStatusUpdateEvent { TaskId = "t1", Status = new TaskStatus { State = TaskState.Submitted } } };
        var r2 = new StreamResponse { StatusUpdate = new TaskStatusUpdateEvent { TaskId = "t1", Status = new TaskStatus { State = TaskState.Working } } };
        var r3 = new StreamResponse { StatusUpdate = new TaskStatusUpdateEvent { TaskId = "t1", Status = new TaskStatus { State = TaskState.Completed } } };
        await queue.WriteAsync(r1);
        await queue.WriteAsync(r2);
        await queue.WriteAsync(r3);
        queue.Complete();

        // Act
        List<StreamResponse> yielded = [];
        await foreach (var e in queue)
        {
            yielded.Add(e);
        }

        // Assert
        Assert.Equal(3, yielded.Count);
        Assert.Equal(TaskState.Submitted, yielded[0].StatusUpdate!.Status.State);
        Assert.Equal(TaskState.Working, yielded[1].StatusUpdate!.Status.State);
        Assert.Equal(TaskState.Completed, yielded[2].StatusUpdate!.Status.State);
    }

    [Fact]
    public async Task Enumerator_ShouldSupportCancellation()
    {
        // Arrange
        var queue = new AgentEventQueue();
        var cts = new CancellationTokenSource(50);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in queue.WithCancellation(cts.Token))
            {
                // Should not yield - will time out and cancel
            }
        });
    }

    [Fact]
    public async Task WriteAsync_MessageResponse_ShouldYield()
    {
        // Arrange
        var queue = new AgentEventQueue();
        var response = new StreamResponse
        {
            Message = new Message { Role = Role.Agent, Parts = [Part.FromText("hello")] }
        };
        await queue.WriteAsync(response);
        queue.Complete();

        // Act
        List<StreamResponse> yielded = [];
        await foreach (var e in queue)
        {
            yielded.Add(e);
        }

        // Assert
        Assert.Single(yielded);
        Assert.NotNull(yielded[0].Message);
        Assert.Equal("hello", yielded[0].Message!.Parts[0].Text);
    }

    [Fact]
    public async Task EnqueueTaskAsync_ShouldYieldTaskResponse()
    {
        // Arrange
        var queue = new AgentEventQueue();
        var task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } };
        await queue.EnqueueTaskAsync(task);
        queue.Complete();

        // Act
        List<StreamResponse> yielded = [];
        await foreach (var e in queue)
        {
            yielded.Add(e);
        }

        // Assert
        Assert.Single(yielded);
        Assert.NotNull(yielded[0].Task);
        Assert.Equal("t1", yielded[0].Task!.Id);
    }

    [Fact]
    public async Task EnqueueMessageAsync_ShouldYieldMessageResponse()
    {
        // Arrange
        var queue = new AgentEventQueue();
        var message = new Message { Role = Role.Agent, MessageId = "m1", Parts = [Part.FromText("hi")] };
        await queue.EnqueueMessageAsync(message);
        queue.Complete();

        // Act
        List<StreamResponse> yielded = [];
        await foreach (var e in queue)
        {
            yielded.Add(e);
        }

        // Assert
        Assert.Single(yielded);
        Assert.NotNull(yielded[0].Message);
        Assert.Equal("m1", yielded[0].Message!.MessageId);
    }
}

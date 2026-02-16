namespace A2A.UnitTests.Server;

public class TaskUpdateEventEnumeratorTests
{
    [Fact]
    public async Task WriteAsync_ShouldYieldResponse()
    {
        // Arrange
        var enumerator = new TaskUpdateEventEnumerator();
        var response = new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent { TaskId = "t1", Status = new TaskStatus { State = TaskState.Submitted } }
        };
        await enumerator.WriteAsync(response);
        enumerator.Complete();

        // Act
        List<StreamResponse> yielded = [];
        await foreach (var e in enumerator)
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
        var enumerator = new TaskUpdateEventEnumerator();
        var r1 = new StreamResponse { StatusUpdate = new TaskStatusUpdateEvent { TaskId = "t1", Status = new TaskStatus { State = TaskState.Submitted } } };
        var r2 = new StreamResponse { StatusUpdate = new TaskStatusUpdateEvent { TaskId = "t1", Status = new TaskStatus { State = TaskState.Working } } };
        var r3 = new StreamResponse { StatusUpdate = new TaskStatusUpdateEvent { TaskId = "t1", Status = new TaskStatus { State = TaskState.Completed } } };
        await enumerator.WriteAsync(r1);
        await enumerator.WriteAsync(r2);
        await enumerator.WriteAsync(r3);
        enumerator.Complete();

        // Act
        List<StreamResponse> yielded = [];
        await foreach (var e in enumerator)
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
        var enumerator = new TaskUpdateEventEnumerator();
        var cts = new CancellationTokenSource(50);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in enumerator.WithCancellation(cts.Token))
            {
                // Should not yield - will time out and cancel
            }
        });
    }

    [Fact]
    public async Task WriteAsync_MessageResponse_ShouldYield()
    {
        // Arrange
        var enumerator = new TaskUpdateEventEnumerator();
        var response = new StreamResponse
        {
            Message = new Message { Role = Role.Agent, Parts = [Part.FromText("hello")] }
        };
        await enumerator.WriteAsync(response);
        enumerator.Complete();

        // Act
        List<StreamResponse> yielded = [];
        await foreach (var e in enumerator)
        {
            yielded.Add(e);
        }

        // Assert
        Assert.Single(yielded);
        Assert.NotNull(yielded[0].Message);
        Assert.Equal("hello", yielded[0].Message!.Parts[0].Text);
    }
}

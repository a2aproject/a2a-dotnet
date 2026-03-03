namespace A2A.UnitTests.Server;

public class InMemoryTaskStoreTests
{
    [Fact]
    public async Task SaveAndGetTask_ShouldRoundTrip()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Submitted },
        };

        // Act
        await sut.SaveTaskAsync("t1", task);
        var result = await sut.GetTaskAsync("t1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("t1", result!.Id);
        Assert.Equal("ctx-1", result.ContextId);
        Assert.Equal(TaskState.Submitted, result.Status.State);
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsNull_WhenNotExists()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        // Act
        var result = await sut.GetTaskAsync("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveTaskAsync_ShouldUpsert()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Submitted },
        };
        await sut.SaveTaskAsync("t1", task);

        // Act — modify and save again
        task.Status = new TaskStatus { State = TaskState.Working };
        await sut.SaveTaskAsync("t1", task);

        // Assert
        var result = await sut.GetTaskAsync("t1");
        Assert.NotNull(result);
        Assert.Equal(TaskState.Working, result!.Status.State);
    }

    [Fact]
    public async Task DeleteTaskAsync_ShouldRemoveTask()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Submitted },
        };
        await sut.SaveTaskAsync("t1", task);

        // Act
        await sut.DeleteTaskAsync("t1");

        // Assert
        var result = await sut.GetTaskAsync("t1");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteTaskAsync_NoOp_WhenNotExists()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        // Act & Assert — should not throw
        await sut.DeleteTaskAsync("nonexistent");
    }

    [Fact]
    public async Task ListTasksAsync_ShouldReturnAllTasks()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        await sut.SaveTaskAsync("t1", new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } });
        await sut.SaveTaskAsync("t2", new AgentTask { Id = "t2", ContextId = "ctx-2", Status = new TaskStatus { State = TaskState.Working } });

        // Act
        var result = await sut.ListTasksAsync(new ListTasksRequest());

        // Assert
        Assert.NotNull(result.Tasks);
        Assert.Equal(2, result.Tasks!.Count);
        Assert.Equal(2, result.TotalSize);
    }

    [Fact]
    public async Task ListTasksAsync_ShouldFilterByContextId()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        await sut.SaveTaskAsync("t1", new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } });
        await sut.SaveTaskAsync("t2", new AgentTask { Id = "t2", ContextId = "ctx-2", Status = new TaskStatus { State = TaskState.Working } });

        // Act
        var result = await sut.ListTasksAsync(new ListTasksRequest { ContextId = "ctx-1" });

        // Assert
        Assert.NotNull(result.Tasks);
        Assert.Single(result.Tasks!);
        Assert.Equal("t1", result.Tasks![0].Id);
    }

    [Fact]
    public async Task ListTasksAsync_ShouldFilterByStatus()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        await sut.SaveTaskAsync("t1", new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } });
        await sut.SaveTaskAsync("t2", new AgentTask { Id = "t2", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Completed } });

        // Act
        var result = await sut.ListTasksAsync(new ListTasksRequest { Status = TaskState.Completed });

        // Assert
        Assert.NotNull(result.Tasks);
        Assert.Single(result.Tasks!);
        Assert.Equal("t2", result.Tasks![0].Id);
    }

    [Fact]
    public async Task ListTasksAsync_ShouldNotMutateStoredData()
    {
        // Arrange — regression test: mutating a returned task must not affect stored state
        var sut = new InMemoryTaskStore();
        await sut.SaveTaskAsync("t1", new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
            History = [new Message { MessageId = "m1", Parts = [Part.FromText("original")] }],
        });

        // Act — get task, mutate it
        var first = await sut.GetTaskAsync("t1");
        first!.History!.Add(new Message { MessageId = "m-injected", Parts = [Part.FromText("injected")] });
        first.Status = new TaskStatus { State = TaskState.Completed };

        // Assert — re-read should return unmutated state
        var second = await sut.GetTaskAsync("t1");
        Assert.NotNull(second);
        Assert.Equal(TaskState.Working, second!.Status.State);
        Assert.Single(second.History!);
        Assert.Equal("m1", second.History![0].MessageId);
    }

    [Fact]
    public async Task ListTasksAsync_ShouldTrimHistoryByHistoryLength()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        await sut.SaveTaskAsync("t1", new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
            History =
            [
                new Message { MessageId = "m1", Parts = [Part.FromText("first")] },
                new Message { MessageId = "m2", Parts = [Part.FromText("second")] },
                new Message { MessageId = "m3", Parts = [Part.FromText("third")] },
            ],
        });

        // Act — request historyLength = 1 (keep only last message)
        var result = await sut.ListTasksAsync(new ListTasksRequest { HistoryLength = 1 });

        // Assert
        Assert.NotNull(result.Tasks);
        Assert.Single(result.Tasks!);
        var task = result.Tasks![0];
        Assert.NotNull(task.History);
        Assert.Single(task.History!);
        Assert.Equal("m3", task.History![0].MessageId);
    }

    [Fact]
    public async Task ListTasksAsync_HistoryLengthZero_ShouldRemoveHistory()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        await sut.SaveTaskAsync("t1", new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
            History = [new Message { MessageId = "m1", Parts = [Part.FromText("data")] }],
        });

        // Act
        var result = await sut.ListTasksAsync(new ListTasksRequest { HistoryLength = 0 });

        // Assert
        var task = result.Tasks![0];
        Assert.Null(task.History);
    }

    [Fact]
    public async Task ListTasksAsync_ShouldPaginate()
    {
        var sut = new InMemoryTaskStore();
        // Save 5 tasks
        for (int i = 1; i <= 5; i++)
            await sut.SaveTaskAsync($"t{i}", new AgentTask { Id = $"t{i}", ContextId = "ctx", Status = new TaskStatus { State = TaskState.Working } });

        // Request page 1 (size 2)
        var page1 = await sut.ListTasksAsync(new ListTasksRequest { PageSize = 2 });
        Assert.Equal(2, page1.Tasks!.Count);
        Assert.Equal(5, page1.TotalSize);
        Assert.False(string.IsNullOrEmpty(page1.NextPageToken));

        // Request page 2 using NextPageToken
        var page2 = await sut.ListTasksAsync(new ListTasksRequest { PageSize = 2, PageToken = page1.NextPageToken });
        Assert.Equal(2, page2.Tasks!.Count);
        Assert.False(string.IsNullOrEmpty(page2.NextPageToken));

        // Request page 3 (last page)
        var page3 = await sut.ListTasksAsync(new ListTasksRequest { PageSize = 2, PageToken = page2.NextPageToken });
        Assert.Single(page3.Tasks!);
        Assert.True(string.IsNullOrEmpty(page3.NextPageToken)); // no more pages
    }

    [Fact]
    public async Task ListTasksAsync_ShouldFilterByStatusTimestampAfter()
    {
        var sut = new InMemoryTaskStore();
        var now = DateTimeOffset.UtcNow;
        await sut.SaveTaskAsync("t-old", new AgentTask { Id = "t-old", ContextId = "ctx", Status = new TaskStatus { State = TaskState.Completed, Timestamp = now.AddHours(-2) } });
        await sut.SaveTaskAsync("t-new", new AgentTask { Id = "t-new", ContextId = "ctx", Status = new TaskStatus { State = TaskState.Working, Timestamp = now } });

        var result = await sut.ListTasksAsync(new ListTasksRequest { StatusTimestampAfter = now.AddHours(-1) });
        Assert.Single(result.Tasks!);
        Assert.Equal("t-new", result.Tasks![0].Id);
    }

    [Fact]
    public async Task ListTasksAsync_ShouldExcludeArtifactsByDefault()
    {
        var sut = new InMemoryTaskStore();
        await sut.SaveTaskAsync("t1", new AgentTask
        {
            Id = "t1", ContextId = "ctx",
            Status = new TaskStatus { State = TaskState.Completed },
            Artifacts = [new Artifact { ArtifactId = "a1", Parts = [Part.FromText("data")] }]
        });

        // Default: artifacts excluded
        var without = await sut.ListTasksAsync(new ListTasksRequest());
        Assert.Null(without.Tasks![0].Artifacts);

        // Explicitly include
        var with = await sut.ListTasksAsync(new ListTasksRequest { IncludeArtifacts = true });
        Assert.NotNull(with.Tasks![0].Artifacts);
        Assert.Single(with.Tasks![0].Artifacts!);
    }

    [Fact]
    public async Task ConcurrentSaveAndGet_ShouldNotCorrupt()
    {
        var sut = new InMemoryTaskStore();
        await sut.SaveTaskAsync("t1", new AgentTask { Id = "t1", ContextId = "ctx", Status = new TaskStatus { State = TaskState.Submitted } });

        // Run 50 concurrent saves + gets
        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
        {
            var state = i % 2 == 0 ? TaskState.Working : TaskState.Completed;
            await sut.SaveTaskAsync("t1", new AgentTask { Id = "t1", ContextId = "ctx", Status = new TaskStatus { State = state } });
            var result = await sut.GetTaskAsync("t1");
            Assert.NotNull(result);
            Assert.Equal("t1", result!.Id);
        }));

        await Task.WhenAll(tasks); // Should not throw, corrupt, or deadlock
    }
}

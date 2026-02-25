namespace A2A.UnitTests.Server;

public class InMemoryEventStoreTests
{
    [Fact]
    public async Task AppendAsync_And_GetTaskAsync_ShouldProjectTask()
    {
        // Arrange
        var sut = new InMemoryEventStore();
        var task = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Submitted },
        };

        // Act
        await sut.AppendAsync("t1", new StreamResponse { Task = task });
        var result = await sut.GetTaskAsync("t1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("t1", result!.Id);
        Assert.Equal(TaskState.Submitted, result.Status.State);
    }

    [Fact]
    public async Task GetTaskAsync_ShouldReturnNull_WhenNoEvents()
    {
        // Arrange
        var sut = new InMemoryEventStore();

        // Act
        var result = await sut.GetTaskAsync("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task AppendAsync_StatusUpdate_ShouldUpdateProjection()
    {
        // Arrange
        var sut = new InMemoryEventStore();
        await sut.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask
            {
                Id = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Submitted },
            }
        });

        // Act
        await sut.AppendAsync("t1", new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Working },
            }
        });

        // Assert
        var result = await sut.GetTaskAsync("t1");
        Assert.NotNull(result);
        Assert.Equal(TaskState.Working, result!.Status.State);
    }

    [Fact]
    public async Task AppendAsync_ArtifactUpdate_ShouldUpdateProjection()
    {
        // Arrange
        var sut = new InMemoryEventStore();
        await sut.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask
            {
                Id = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Working },
            }
        });

        // Act
        await sut.AppendAsync("t1", new StreamResponse
        {
            ArtifactUpdate = new TaskArtifactUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Artifact = new Artifact { ArtifactId = "a1", Parts = [Part.FromText("result")] },
            }
        });

        // Assert
        var result = await sut.GetTaskAsync("t1");
        Assert.NotNull(result);
        Assert.NotNull(result!.Artifacts);
        Assert.Single(result.Artifacts);
        Assert.Equal("a1", result.Artifacts[0].ArtifactId);
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnEventsFromVersion()
    {
        // Arrange
        var sut = new InMemoryEventStore();
        await sut.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } }
        });
        await sut.AppendAsync("t1", new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent { TaskId = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Working } }
        });
        await sut.AppendAsync("t1", new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent { TaskId = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Completed } }
        });

        // Act — read from version 1 (skipping the initial Task event at version 0)
        var events = new List<EventEnvelope>();
        await foreach (var e in sut.ReadAsync("t1", fromVersion: 1))
        {
            events.Add(e);
        }

        // Assert
        Assert.Equal(2, events.Count);
        Assert.Equal(1, events[0].Version);
        Assert.Equal(2, events[1].Version);
        Assert.NotNull(events[0].Event.StatusUpdate);
        Assert.NotNull(events[1].Event.StatusUpdate);
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnEmpty_WhenNoEvents()
    {
        // Arrange
        var sut = new InMemoryEventStore();

        // Act
        var events = new List<EventEnvelope>();
        await foreach (var e in sut.ReadAsync("nonexistent"))
        {
            events.Add(e);
        }

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnTrue_WhenEventsExist()
    {
        // Arrange
        var sut = new InMemoryEventStore();
        await sut.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } }
        });

        // Act & Assert
        Assert.True(await sut.ExistsAsync("t1"));
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnFalse_WhenNoEvents()
    {
        // Arrange
        var sut = new InMemoryEventStore();

        // Act & Assert
        Assert.False(await sut.ExistsAsync("nonexistent"));
    }

    [Fact]
    public async Task ListTasksAsync_ShouldReturnAllTasks()
    {
        // Arrange
        var sut = new InMemoryEventStore();
        await sut.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } }
        });
        await sut.AppendAsync("t2", new StreamResponse
        {
            Task = new AgentTask { Id = "t2", ContextId = "ctx-2", Status = new TaskStatus { State = TaskState.Working } }
        });

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
        var sut = new InMemoryEventStore();
        await sut.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } }
        });
        await sut.AppendAsync("t2", new StreamResponse
        {
            Task = new AgentTask { Id = "t2", ContextId = "ctx-2", Status = new TaskStatus { State = TaskState.Working } }
        });

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
        var sut = new InMemoryEventStore();
        await sut.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } }
        });
        await sut.AppendAsync("t2", new StreamResponse
        {
            Task = new AgentTask { Id = "t2", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Completed } }
        });

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
        // Arrange — regression test: mutating a returned projection must not affect stored state
        var sut = new InMemoryEventStore();
        await sut.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask
            {
                Id = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Working },
                History = [new Message { MessageId = "m1", Parts = [Part.FromText("original")] }],
            }
        });

        // Act — get projection, mutate it
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
        var sut = new InMemoryEventStore();
        await sut.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask
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
            }
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
        var sut = new InMemoryEventStore();
        await sut.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask
            {
                Id = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Working },
                History = [new Message { MessageId = "m1", Parts = [Part.FromText("data")] }],
            }
        });

        // Act
        var result = await sut.ListTasksAsync(new ListTasksRequest { HistoryLength = 0 });

        // Assert
        var task = result.Tasks![0];
        Assert.Null(task.History);
    }

    [Fact]
    public async Task AppendAsync_WithExpectedVersion_ShouldEnforceConcurrency()
    {
        // Arrange
        var sut = new InMemoryEventStore();
        await sut.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } }
        });

        // Act & Assert — expect version 0, but log is now at version 1 → conflict
        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            sut.AppendAsync("t1", new StreamResponse
            {
                StatusUpdate = new TaskStatusUpdateEvent
                {
                    TaskId = "t1",
                    ContextId = "ctx-1",
                    Status = new TaskStatus { State = TaskState.Working },
                }
            }, expectedVersion: 0));
        Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
    }

    [Fact]
    public async Task SubscribeAsync_ShouldDeliverCatchUpEvents()
    {
        // Arrange
        var sut = new InMemoryEventStore();
        await sut.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } }
        });
        await sut.AppendAsync("t1", new StreamResponse
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
        await foreach (var e in sut.SubscribeAsync("t1", afterVersion: -1, cts.Token))
        {
            events.Add(e);
            if (events.Count >= 2) break;
        }

        // Assert — should see both events via catch-up
        Assert.Equal(2, events.Count);
        Assert.Equal(0, events[0].Version);
        Assert.Equal(1, events[1].Version);
    }

    [Fact]
    public async Task SubscribeAsync_ShouldDeliverLiveEvents()
    {
        // Arrange
        var sut = new InMemoryEventStore();
        await sut.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Submitted } }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new List<EventEnvelope>();

        // Act — subscribe from version 0 (skip catch-up of initial event), then append
        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var e in sut.SubscribeAsync("t1", afterVersion: 0, cts.Token))
            {
                events.Add(e);
                break; // Only need the first live event
            }
        }, cts.Token);

        // Give subscriber time to register its channel
        await Task.Delay(100, cts.Token);

        await sut.AppendAsync("t1", new StreamResponse
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
        var sut = new InMemoryEventStore();
        await sut.AppendAsync("t1", new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Working } }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new List<EventEnvelope>();

        // Act — subscribe, then push a terminal status
        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var e in sut.SubscribeAsync("t1", afterVersion: 0, cts.Token))
            {
                events.Add(e);
                break; // Collect just the terminal event
            }
        }, cts.Token);

        await Task.Delay(100, cts.Token);

        await sut.AppendAsync("t1", new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Failed },
            }
        });

        // Subscription task should complete promptly
        await subscribeTask;

        // Assert
        Assert.Single(events);
        Assert.Equal(TaskState.Failed, events[0].Event.StatusUpdate!.Status.State);
    }
}

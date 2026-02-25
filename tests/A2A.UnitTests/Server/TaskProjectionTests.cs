namespace A2A.UnitTests.Server;

public class TaskProjectionTests
{
    [Fact]
    public void Apply_WithTaskEvent_ReturnsTask()
    {
        // Arrange
        var task = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Submitted },
        };
        var evt = new StreamResponse { Task = task };

        // Act
        var result = TaskProjection.Apply(null, evt);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("t1", result!.Id);
        Assert.Equal(TaskState.Submitted, result.Status.State);
    }

    [Fact]
    public void Apply_WithStatusUpdate_UpdatesStatus()
    {
        // Arrange
        var current = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Submitted },
        };
        var evt = new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Working },
            }
        };

        // Act
        var result = TaskProjection.Apply(current, evt);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TaskState.Working, result!.Status.State);
    }

    [Fact]
    public void Apply_WithStatusUpdate_MovesSupersededMessageToHistory()
    {
        // Arrange
        var statusMessage = new Message
        {
            MessageId = "sm1",
            Role = Role.Agent,
            Parts = [Part.FromText("working on it")],
        };
        var current = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working, Message = statusMessage },
        };
        var evt = new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Completed },
            }
        };

        // Act
        var result = TaskProjection.Apply(current, evt);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TaskState.Completed, result!.Status.State);
        Assert.NotNull(result.History);
        Assert.Single(result.History);
        Assert.Equal("sm1", result.History[0].MessageId);
    }

    [Fact]
    public void Apply_WithArtifactUpdate_AddsNewArtifact()
    {
        // Arrange
        var current = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
        };
        var evt = new StreamResponse
        {
            ArtifactUpdate = new TaskArtifactUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Append = false,
                Artifact = new Artifact { ArtifactId = "a1", Parts = [Part.FromText("data")] },
            }
        };

        // Act
        var result = TaskProjection.Apply(current, evt);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result!.Artifacts);
        Assert.Single(result.Artifacts);
        Assert.Equal("a1", result.Artifacts[0].ArtifactId);
    }

    [Fact]
    public void Apply_WithArtifactUpdate_ReplacesExistingArtifact()
    {
        // Arrange
        var current = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
            Artifacts = [new Artifact { ArtifactId = "a1", Parts = [Part.FromText("old")] }],
        };
        var evt = new StreamResponse
        {
            ArtifactUpdate = new TaskArtifactUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Append = false,
                Artifact = new Artifact { ArtifactId = "a1", Parts = [Part.FromText("new")] },
            }
        };

        // Act
        var result = TaskProjection.Apply(current, evt);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result!.Artifacts!);
        Assert.Equal("new", result.Artifacts![0].Parts[0].Text);
    }

    [Fact]
    public void Apply_WithArtifactUpdate_AppendExtendsParts()
    {
        // Arrange
        var current = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
            Artifacts = [new Artifact { ArtifactId = "a1", Parts = [Part.FromText("chunk1")] }],
        };
        var evt = new StreamResponse
        {
            ArtifactUpdate = new TaskArtifactUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Append = true,
                Artifact = new Artifact { ArtifactId = "a1", Parts = [Part.FromText("chunk2")] },
            }
        };

        // Act
        var result = TaskProjection.Apply(current, evt);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result!.Artifacts!);
        Assert.Equal(2, result.Artifacts![0].Parts.Count);
        Assert.Equal("chunk1", result.Artifacts[0].Parts[0].Text);
        Assert.Equal("chunk2", result.Artifacts[0].Parts[1].Text);
    }

    [Fact]
    public void Apply_WithArtifactUpdate_AppendAddsNonexistent()
    {
        // Arrange
        var current = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
            Artifacts = [new Artifact { ArtifactId = "a1", Parts = [Part.FromText("data")] }],
        };
        var evt = new StreamResponse
        {
            ArtifactUpdate = new TaskArtifactUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Append = true,
                Artifact = new Artifact { ArtifactId = "a-new", Parts = [Part.FromText("extra")] },
            }
        };

        // Act
        var result = TaskProjection.Apply(current, evt);

        // Assert — non-existent artifact added when append=true
        Assert.NotNull(result);
        Assert.Equal(2, result!.Artifacts!.Count);
        Assert.Equal("a1", result.Artifacts[0].ArtifactId);
        Assert.Equal("a-new", result.Artifacts[1].ArtifactId);
    }

    [Fact]
    public void Apply_WithMessage_AppendsToHistory()
    {
        // Arrange
        var current = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
        };
        var msg = new Message
        {
            MessageId = "m1",
            Role = Role.Agent,
            Parts = [Part.FromText("hello")],
        };
        var evt = new StreamResponse { Message = msg };

        // Act
        var result = TaskProjection.Apply(current, evt);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result!.History);
        Assert.Single(result.History);
        Assert.Equal("m1", result.History[0].MessageId);
    }

    [Fact]
    public async Task ReplayAsync_FullLifecycle()
    {
        // Arrange
        var events = new List<EventEnvelope>
        {
            new(0, new StreamResponse
            {
                Task = new AgentTask
                {
                    Id = "t1",
                    ContextId = "ctx-1",
                    Status = new TaskStatus { State = TaskState.Submitted },
                }
            }),
            new(1, new StreamResponse
            {
                StatusUpdate = new TaskStatusUpdateEvent
                {
                    TaskId = "t1",
                    ContextId = "ctx-1",
                    Status = new TaskStatus { State = TaskState.Working },
                }
            }),
            new(2, new StreamResponse
            {
                ArtifactUpdate = new TaskArtifactUpdateEvent
                {
                    TaskId = "t1",
                    ContextId = "ctx-1",
                    Artifact = new Artifact { ArtifactId = "a1", Parts = [Part.FromText("result")] },
                }
            }),
            new(3, new StreamResponse
            {
                StatusUpdate = new TaskStatusUpdateEvent
                {
                    TaskId = "t1",
                    ContextId = "ctx-1",
                    Status = new TaskStatus { State = TaskState.Completed },
                }
            }),
        };

        // Act
        var result = await TaskProjection.ReplayAsync(events.ToAsyncEnumerable());

        // Assert
        Assert.NotNull(result);
        Assert.Equal("t1", result!.Id);
        Assert.Equal(TaskState.Completed, result.Status.State);
        Assert.NotNull(result.Artifacts);
        Assert.Single(result.Artifacts);
        Assert.Equal("a1", result.Artifacts[0].ArtifactId);
    }

    [Fact]
    public async Task ReplayAsync_EmptyStream_ReturnsNull()
    {
        // Arrange
        var events = Array.Empty<EventEnvelope>();

        // Act
        var result = await TaskProjection.ReplayAsync(events.ToAsyncEnumerable());

        // Assert
        Assert.Null(result);
    }
}

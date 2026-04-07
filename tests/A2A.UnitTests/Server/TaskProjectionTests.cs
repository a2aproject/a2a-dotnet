using System.Text.Json;

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
    public void Apply_WithArtifactUpdate_AppendUpsertsMetadata()
    {
        // Arrange
        var current = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
            Artifacts =
            [
                new Artifact
                {
                    ArtifactId = "a1",
                    Parts = [Part.FromText("data")],
                    Metadata = new Dictionary<string, JsonElement>
                    {
                        ["key1"] = JsonSerializer.SerializeToElement("old_value"),
                    },
                },
            ],
        };
        var evt = new StreamResponse
        {
            ArtifactUpdate = new TaskArtifactUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Append = true,
                Artifact = new Artifact
                {
                    ArtifactId = "a1",
                    Parts = [],
                    Metadata = new Dictionary<string, JsonElement>
                    {
                        ["key1"] = JsonSerializer.SerializeToElement("new_value"),
                        ["key2"] = JsonSerializer.SerializeToElement("added"),
                    },
                },
            }
        };

        // Act
        var result = TaskProjection.Apply(current, evt);

        // Assert
        Assert.NotNull(result);
        var metadata = result!.Artifacts![0].Metadata!;
        Assert.Equal(2, metadata.Count);
        Assert.Equal("new_value", metadata["key1"].GetString());
        Assert.Equal("added", metadata["key2"].GetString());
    }

    [Fact]
    public void Apply_WithArtifactUpdate_AppendDeduplicatesExtensions()
    {
        // Arrange
        var current = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
            Artifacts =
            [
                new Artifact
                {
                    ArtifactId = "a1",
                    Parts = [Part.FromText("data")],
                    Extensions = ["ext1", "ext2"],
                },
            ],
        };
        var evt = new StreamResponse
        {
            ArtifactUpdate = new TaskArtifactUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Append = true,
                Artifact = new Artifact
                {
                    ArtifactId = "a1",
                    Parts = [],
                    Extensions = ["ext2", "ext3"],
                },
            }
        };

        // Act
        var result = TaskProjection.Apply(current, evt);

        // Assert
        Assert.NotNull(result);
        var extensions = result!.Artifacts![0].Extensions!;
        Assert.Equal(3, extensions.Count);
        Assert.Equal(["ext1", "ext2", "ext3"], extensions);
    }

    [Fact]
    public void Apply_WithArtifactUpdate_AppendUpdatesNameAndDescription()
    {
        // Arrange
        var current = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
            Artifacts =
            [
                new Artifact
                {
                    ArtifactId = "a1",
                    Parts = [Part.FromText("data")],
                    Name = "Original",
                },
            ],
        };
        var evt = new StreamResponse
        {
            ArtifactUpdate = new TaskArtifactUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Append = true,
                Artifact = new Artifact
                {
                    ArtifactId = "a1",
                    Parts = [],
                    Name = "Updated",
                    Description = "New desc",
                },
            }
        };

        // Act
        var result = TaskProjection.Apply(current, evt);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Updated", result!.Artifacts![0].Name);
        Assert.Equal("New desc", result.Artifacts[0].Description);
    }

    [Fact]
    public void Apply_WithArtifactUpdate_AppendPreservesNameWhenIncomingEmpty()
    {
        // Arrange
        var current = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
            Artifacts =
            [
                new Artifact
                {
                    ArtifactId = "a1",
                    Parts = [Part.FromText("data")],
                    Name = "Keep This",
                    Description = "Keep Desc",
                },
            ],
        };
        var evt = new StreamResponse
        {
            ArtifactUpdate = new TaskArtifactUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Append = true,
                Artifact = new Artifact
                {
                    ArtifactId = "a1",
                    Parts = [],
                },
            }
        };

        // Act
        var result = TaskProjection.Apply(current, evt);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Keep This", result!.Artifacts![0].Name);
        Assert.Equal("Keep Desc", result.Artifacts[0].Description);
    }

    [Fact]
    public void Apply_WithArtifactUpdate_AppendInitializesMetadataWhenNull()
    {
        // Arrange
        var current = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-1",
            Status = new TaskStatus { State = TaskState.Working },
            Artifacts =
            [
                new Artifact
                {
                    ArtifactId = "a1",
                    Parts = [Part.FromText("data")],
                    Metadata = null,
                },
            ],
        };
        var evt = new StreamResponse
        {
            ArtifactUpdate = new TaskArtifactUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx-1",
                Append = true,
                Artifact = new Artifact
                {
                    ArtifactId = "a1",
                    Parts = [],
                    Metadata = new Dictionary<string, JsonElement>
                    {
                        ["key1"] = JsonSerializer.SerializeToElement("value1"),
                    },
                },
            }
        };

        // Act
        var result = TaskProjection.Apply(current, evt);

        // Assert
        Assert.NotNull(result);
        var metadata = result!.Artifacts![0].Metadata;
        Assert.NotNull(metadata);
        Assert.Single(metadata!);
        Assert.Equal("value1", metadata["key1"].GetString());
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
}

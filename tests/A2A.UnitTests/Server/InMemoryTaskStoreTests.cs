namespace A2A.UnitTests.Server;

public class InMemoryTaskStoreTests
{
    [Fact]
    public async Task SetTaskAsync_And_GetTaskAsync_ShouldStoreAndRetrieveTask()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };

        // Act
        await sut.SetTaskAsync(task);
        var result = await sut.GetTaskAsync("task1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("task1", result!.Id);
        Assert.Equal(TaskState.Submitted, result.Status.State);
    }

    [Fact]
    public async Task GetTaskAsync_ShouldReturnNull_WhenTaskDoesNotExist()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        // Act
        var result = await sut.GetTaskAsync("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldUpdateTaskStatus()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask { Id = "task2", Status = new AgentTaskStatus { State = TaskState.Submitted } };
        await sut.SetTaskAsync(task);
        var message = new AgentMessage { MessageId = "msg1" };

        // Act
        var status = await sut.UpdateStatusAsync("task2", TaskState.Working, message);
        var updatedTask = await sut.GetTaskAsync("task2");

        // Assert
        Assert.Equal(TaskState.Working, status.State);
        Assert.Equal(TaskState.Working, updatedTask!.Status.State);
        Assert.Equal("msg1", status.Message!.MessageId);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldThrow_WhenTaskDoesNotExist()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() => sut.UpdateStatusAsync("notfound", TaskState.Completed));
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task GetPushNotificationAsync_ShouldReturnNull_WhenTaskDoesNotExist()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        // Act
        var result = await sut.GetPushNotificationAsync("missing", "config-missing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPushNotificationAsync_ShouldReturnNull_WhenConfigDoesNotExist()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        await sut.SetPushNotificationConfigAsync(new TaskPushNotificationConfig { TaskId = "task-id", PushNotificationConfig = new() { Id = "config-id" } });

        // Act
        var result = await sut.GetPushNotificationAsync("task-id", "config-missing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPushNotificationAsync_ShouldReturnCorrectConfig_WhenMultipleConfigsExistForSameTask()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var taskId = "task-with-multiple-configs";

        var config1 = new TaskPushNotificationConfig
        {
            TaskId = taskId,
            PushNotificationConfig = new PushNotificationConfig
            {
                Url = "http://config1",
                Id = "config-id-1",
                Token = "token1"
            }
        };

        var config2 = new TaskPushNotificationConfig
        {
            TaskId = taskId,
            PushNotificationConfig = new PushNotificationConfig
            {
                Url = "http://config2",
                Id = "config-id-2",
                Token = "token2"
            }
        };

        var config3 = new TaskPushNotificationConfig
        {
            TaskId = taskId,
            PushNotificationConfig = new PushNotificationConfig
            {
                Url = "http://config3",
                Id = "config-id-3",
                Token = "token3"
            }
        };

        // Act - Store multiple configs for the same task
        await sut.SetPushNotificationConfigAsync(config1);
        await sut.SetPushNotificationConfigAsync(config2);
        await sut.SetPushNotificationConfigAsync(config3);

        // Get specific configs by both taskId and notificationConfigId
        var result1 = await sut.GetPushNotificationAsync(taskId, "config-id-1");
        var result2 = await sut.GetPushNotificationAsync(taskId, "config-id-2");
        var result3 = await sut.GetPushNotificationAsync(taskId, "config-id-3");
        var resultNotFound = await sut.GetPushNotificationAsync(taskId, "non-existent-config");

        // Assert - Verify each call returns the correct specific config
        Assert.NotNull(result1);
        Assert.Equal(taskId, result1!.TaskId);
        Assert.Equal("config-id-1", result1.PushNotificationConfig.Id);
        Assert.Equal("http://config1", result1.PushNotificationConfig.Url);
        Assert.Equal("token1", result1.PushNotificationConfig.Token);

        Assert.NotNull(result2);
        Assert.Equal(taskId, result2!.TaskId);
        Assert.Equal("config-id-2", result2.PushNotificationConfig.Id);
        Assert.Equal("http://config2", result2.PushNotificationConfig.Url);
        Assert.Equal("token2", result2.PushNotificationConfig.Token);

        Assert.NotNull(result3);
        Assert.Equal(taskId, result3!.TaskId);
        Assert.Equal("config-id-3", result3.PushNotificationConfig.Id);
        Assert.Equal("http://config3", result3.PushNotificationConfig.Url);
        Assert.Equal("token3", result3.PushNotificationConfig.Token);

        Assert.Null(resultNotFound);
    }

    [Fact]
    public async Task GetPushNotificationsAsync_ShouldReturnEmptyList_WhenNoConfigsExistForTask()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        // Act
        var result = await sut.GetPushNotificationsAsync("task-without-configs");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTaskAsync_ShouldReturnCanceledTask_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var task = sut.GetTaskAsync("test-id", cts.Token);

        // Assert
        Assert.True(task.IsCanceled);
        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
    }

    [Fact]
    public async Task GetPushNotificationAsync_ShouldReturnCanceledTask_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var task = sut.GetPushNotificationAsync("test-id", "config-id", cts.Token);

        // Assert
        Assert.True(task.IsCanceled);
        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldReturnCanceledTask_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var task = sut.UpdateStatusAsync("test-id", TaskState.Working, cancellationToken: cts.Token);

        // Assert
        Assert.True(task.IsCanceled);
        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
    }

    [Fact]
    public async Task SetTaskAsync_ShouldReturnCanceledTask_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var agentTask = new AgentTask { Id = "test-id", Status = new AgentTaskStatus { State = TaskState.Submitted } };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var task = sut.SetTaskAsync(agentTask, cts.Token);

        // Assert
        Assert.True(task.IsCanceled);
        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
    }

    [Fact]
    public async Task SetPushNotificationConfigAsync_ShouldReturnCanceledTask_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var config = new TaskPushNotificationConfig();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var task = sut.SetPushNotificationConfigAsync(config, cts.Token);

        // Assert
        Assert.True(task.IsCanceled);
        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
    }

    [Fact]
    public async Task GetPushNotificationsAsync_ShouldReturnCanceledTask_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var sut = new InMemoryTaskStore();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var task = sut.GetPushNotificationsAsync("test-id", cts.Token);

        // Assert
        Assert.True(task.IsCanceled);
        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
    }

    [Fact]
    public async Task UpdateArtifactAsync_WithAppendFalse_AddsNewArtifact()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };
        await sut.SetTaskAsync(task);

        var artifact = new Artifact
        {
            ArtifactId = "artifact1",
            Name = "Test Artifact",
            Parts = [new TextPart { Text = "Content" }]
        };

        // Act
        await sut.UpdateArtifactAsync("task1", artifact, append: false, lastChunk: true);
        var result = await sut.GetTaskAsync("task1");

        // Assert
        Assert.NotNull(result?.Artifacts);
        Assert.Single(result!.Artifacts);
        Assert.Equal("artifact1", result.Artifacts[0].ArtifactId);
        Assert.Equal("Test Artifact", result.Artifacts[0].Name);
    }

    [Fact]
    public async Task UpdateArtifactAsync_WithAppendTrue_AppendsToExistingArtifact()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };
        await sut.SetTaskAsync(task);

        var artifact1 = new Artifact
        {
            ArtifactId = "artifact1",
            Name = "Story",
            Parts = [new TextPart { Text = "Part 1. " }]
        };

        var artifact2 = new Artifact
        {
            ArtifactId = "artifact1",
            Parts = [new TextPart { Text = "Part 2." }]
        };

        // Act
        await sut.UpdateArtifactAsync("task1", artifact1, append: false, lastChunk: false);
        await sut.UpdateArtifactAsync("task1", artifact2, append: true, lastChunk: true);
        var result = await sut.GetTaskAsync("task1");

        // Assert
        Assert.NotNull(result?.Artifacts);
        Assert.Single(result!.Artifacts);
        Assert.Equal("artifact1", result.Artifacts[0].ArtifactId);
        Assert.Equal(2, result.Artifacts[0].Parts.Count);
        Assert.Equal("Part 1. ", result.Artifacts[0].Parts[0].AsTextPart().Text);
        Assert.Equal("Part 2.", result.Artifacts[0].Parts[1].AsTextPart().Text);
    }

    [Fact]
    public async Task UpdateArtifactAsync_WithAppendTrue_CreatesNewArtifactIfNotExists()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };
        await sut.SetTaskAsync(task);

        var artifact = new Artifact
        {
            ArtifactId = "artifact1",
            Parts = [new TextPart { Text = "Content" }]
        };

        // Act - append to non-existent artifact should create it
        await sut.UpdateArtifactAsync("task1", artifact, append: true, lastChunk: true);
        var result = await sut.GetTaskAsync("task1");

        // Assert
        Assert.NotNull(result?.Artifacts);
        Assert.Single(result!.Artifacts);
        Assert.Equal("artifact1", result.Artifacts[0].ArtifactId);
    }

    [Fact]
    public async Task UpdateArtifactAsync_WithAppendFalse_ReplacesExistingArtifact()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };
        await sut.SetTaskAsync(task);

        var artifact1 = new Artifact
        {
            ArtifactId = "artifact1",
            Name = "Original",
            Parts = [new TextPart { Text = "Old Content" }]
        };

        var artifact2 = new Artifact
        {
            ArtifactId = "artifact1",
            Name = "Updated",
            Parts = [new TextPart { Text = "New Content" }]
        };

        // Act
        await sut.UpdateArtifactAsync("task1", artifact1, append: false, lastChunk: false);
        await sut.UpdateArtifactAsync("task1", artifact2, append: false, lastChunk: true);
        var result = await sut.GetTaskAsync("task1");

        // Assert
        Assert.NotNull(result?.Artifacts);
        Assert.Single(result!.Artifacts);
        Assert.Equal("Updated", result.Artifacts[0].Name);
        Assert.Single(result.Artifacts[0].Parts);
        Assert.Equal("New Content", result.Artifacts[0].Parts[0].AsTextPart().Text);
    }

    [Fact]
    public async Task UpdateArtifactAsync_MergesMetadataAndExtensions()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };
        await sut.SetTaskAsync(task);

        var artifact1 = new Artifact
        {
            ArtifactId = "artifact1",
            Parts = [new TextPart { Text = "Content" }],
            Metadata = new() { ["key1"] = System.Text.Json.JsonDocument.Parse("\"value1\"").RootElement },
            Extensions = ["ext1"]
        };

        var artifact2 = new Artifact
        {
            ArtifactId = "artifact1",
            Parts = [new TextPart { Text = " More" }],
            Metadata = new() { ["key2"] = System.Text.Json.JsonDocument.Parse("\"value2\"").RootElement },
            Extensions = ["ext2"]
        };

        // Act
        await sut.UpdateArtifactAsync("task1", artifact1, append: false, lastChunk: false);
        await sut.UpdateArtifactAsync("task1", artifact2, append: true, lastChunk: true);
        var result = await sut.GetTaskAsync("task1");

        // Assert
        Assert.NotNull(result?.Artifacts);
        Assert.Single(result!.Artifacts);
        Assert.NotNull(result.Artifacts[0].Metadata);
        Assert.Equal(2, result.Artifacts[0].Metadata!.Count);
        Assert.NotNull(result.Artifacts[0].Extensions);
        Assert.Equal(2, result.Artifacts[0].Extensions!.Count);
    }

    [Fact]
    public async Task UpdateArtifactAsync_ThrowsWhenTaskNotFound()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var artifact = new Artifact
        {
            ArtifactId = "artifact1",
            Parts = [new TextPart { Text = "Content" }]
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() => sut.UpdateArtifactAsync("nonexistent", artifact, append: false, lastChunk: true));
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateArtifactAsync_ThrowsWhenArtifactIdIsEmpty()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };
        await sut.SetTaskAsync(task);

        var artifact = new Artifact
        {
            ArtifactId = string.Empty,
            Parts = [new TextPart { Text = "Content" }]
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() => sut.UpdateArtifactAsync("task1", artifact, append: false, lastChunk: true));
        Assert.Equal(A2AErrorCode.InvalidParams, ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateArtifactAsync_ShouldReturnCanceledTask_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var artifact = new Artifact { ArtifactId = "test" };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var task = sut.UpdateArtifactAsync("test-id", artifact, append: false, lastChunk: true, cts.Token);

        // Assert
        Assert.True(task.IsCanceled);
        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
    }

    [Fact]
    public async Task UpdateArtifactAsync_SealedArtifact_ThrowsOnUpdate()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };
        await sut.SetTaskAsync(task);

        var artifactId = "sealed-artifact";

        // Create and seal artifact
        await sut.UpdateArtifactAsync("task1", new Artifact
        {
            ArtifactId = artifactId,
            Name = "Original",
            Parts = [new TextPart { Text = "Original content" }]
        }, append: false, lastChunk: true);

        // Try to update sealed artifact - should throw
        var ex = await Assert.ThrowsAsync<A2AException>(() => sut.UpdateArtifactAsync("task1", new Artifact
        {
            ArtifactId = artifactId,
            Name = "Replacement",
            Parts = [new TextPart { Text = "New content" }]
        }, append: true, lastChunk: true));

        Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
        Assert.Contains("sealed", ex.Message);

        // Verify original artifact is unchanged
        var result = await sut.GetTaskAsync("task1");
        Assert.NotNull(result?.Artifacts);
        Assert.Single(result!.Artifacts);
        Assert.Equal("Original", result.Artifacts[0].Name);
    }

    [Fact]
    public async Task UpdateArtifactAsync_UnsealedArtifact_AllowsAppend()
    {
        // Arrange
        var sut = new InMemoryTaskStore();
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };
        await sut.SetTaskAsync(task);

        var artifactId = "unsealed-artifact";

        // Create artifact without sealing
        await sut.UpdateArtifactAsync("task1", new Artifact
        {
            ArtifactId = artifactId,
            Parts = [new TextPart { Text = "Part 1. " }]
        }, append: false, lastChunk: false);

        // Append to unsealed artifact - should actually append
        await sut.UpdateArtifactAsync("task1", new Artifact
        {
            ArtifactId = artifactId,
            Parts = [new TextPart { Text = "Part 2." }]
        }, append: true, lastChunk: true);

        var result = await sut.GetTaskAsync("task1");

        // Assert
        Assert.NotNull(result?.Artifacts);
        Assert.Single(result!.Artifacts);
        Assert.Equal(2, result.Artifacts[0].Parts.Count);
        Assert.Equal("Part 1. ", result.Artifacts[0].Parts[0].AsTextPart().Text);
        Assert.Equal("Part 2.", result.Artifacts[0].Parts[1].AsTextPart().Text);
    }
}
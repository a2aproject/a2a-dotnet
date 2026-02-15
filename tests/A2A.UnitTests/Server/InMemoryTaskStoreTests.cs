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
}

/// <summary>
/// Tests for <see cref="ArtifactHelper"/>.
/// </summary>
public class ArtifactHelperTests
{
    [Fact]
    public void ApplyArtifactUpdate_WithAppendFalse_AddsNewArtifact()
    {
        // Arrange
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };
        var artifact = new Artifact
        {
            ArtifactId = "artifact1",
            Name = "Test Artifact",
            Parts = [new TextPart { Text = "Content" }]
        };

        // Act
        ArtifactHelper.ApplyArtifactUpdate(task, artifact, append: false);

        // Assert
        Assert.NotNull(task.Artifacts);
        Assert.Single(task.Artifacts);
        Assert.Equal("artifact1", task.Artifacts[0].ArtifactId);
        Assert.Equal("Test Artifact", task.Artifacts[0].Name);
    }

    [Fact]
    public void ApplyArtifactUpdate_WithAppendTrue_AppendsToExistingArtifact()
    {
        // Arrange
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };

        ArtifactHelper.ApplyArtifactUpdate(task, new Artifact
        {
            ArtifactId = "artifact1",
            Name = "Story",
            Parts = [new TextPart { Text = "Part 1. " }]
        }, append: false);

        // Act
        ArtifactHelper.ApplyArtifactUpdate(task, new Artifact
        {
            ArtifactId = "artifact1",
            Parts = [new TextPart { Text = "Part 2." }]
        }, append: true);

        // Assert
        Assert.NotNull(task.Artifacts);
        Assert.Single(task.Artifacts);
        Assert.Equal("artifact1", task.Artifacts[0].ArtifactId);
        Assert.Equal(2, task.Artifacts[0].Parts.Count);
        Assert.Equal("Part 1. ", task.Artifacts[0].Parts[0].AsTextPart().Text);
        Assert.Equal("Part 2.", task.Artifacts[0].Parts[1].AsTextPart().Text);
    }

    [Fact]
    public void ApplyArtifactUpdate_WithAppendTrue_CreatesNewArtifactIfNotExists()
    {
        // Arrange
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };

        // Act - append to non-existent artifact should create it
        ArtifactHelper.ApplyArtifactUpdate(task, new Artifact
        {
            ArtifactId = "artifact1",
            Parts = [new TextPart { Text = "Content" }]
        }, append: true);

        // Assert
        Assert.NotNull(task.Artifacts);
        Assert.Single(task.Artifacts);
        Assert.Equal("artifact1", task.Artifacts[0].ArtifactId);
    }

    [Fact]
    public void ApplyArtifactUpdate_WithAppendFalse_ReplacesExistingArtifact()
    {
        // Arrange
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };

        ArtifactHelper.ApplyArtifactUpdate(task, new Artifact
        {
            ArtifactId = "artifact1",
            Name = "Original",
            Parts = [new TextPart { Text = "Old Content" }]
        }, append: false);

        // Act
        ArtifactHelper.ApplyArtifactUpdate(task, new Artifact
        {
            ArtifactId = "artifact1",
            Name = "Updated",
            Parts = [new TextPart { Text = "New Content" }]
        }, append: false);

        // Assert
        Assert.NotNull(task.Artifacts);
        Assert.Single(task.Artifacts);
        Assert.Equal("Updated", task.Artifacts[0].Name);
        Assert.Single(task.Artifacts[0].Parts);
        Assert.Equal("New Content", task.Artifacts[0].Parts[0].AsTextPart().Text);
    }

    [Fact]
    public void ApplyArtifactUpdate_MergesMetadataAndExtensions()
    {
        // Arrange
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };

        ArtifactHelper.ApplyArtifactUpdate(task, new Artifact
        {
            ArtifactId = "artifact1",
            Parts = [new TextPart { Text = "Content" }],
            Metadata = new() { ["key1"] = System.Text.Json.JsonDocument.Parse("\"value1\"").RootElement },
            Extensions = ["ext1"]
        }, append: false);

        // Act
        ArtifactHelper.ApplyArtifactUpdate(task, new Artifact
        {
            ArtifactId = "artifact1",
            Parts = [new TextPart { Text = " More" }],
            Metadata = new() { ["key2"] = System.Text.Json.JsonDocument.Parse("\"value2\"").RootElement },
            Extensions = ["ext2"]
        }, append: true);

        // Assert
        Assert.NotNull(task.Artifacts);
        Assert.Single(task.Artifacts);
        Assert.Equal(2, task.Artifacts[0].Parts.Count);
        Assert.NotNull(task.Artifacts[0].Metadata);
        Assert.Equal(2, task.Artifacts[0].Metadata!.Count);
        Assert.NotNull(task.Artifacts[0].Extensions);
        Assert.Equal(2, task.Artifacts[0].Extensions!.Count);
    }

    [Fact]
    public void ApplyArtifactUpdate_InitializesArtifactsList()
    {
        // Arrange - task with null Artifacts
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };
        Assert.Null(task.Artifacts);

        // Act
        ArtifactHelper.ApplyArtifactUpdate(task, new Artifact
        {
            ArtifactId = "artifact1",
            Parts = [new TextPart { Text = "Content" }]
        }, append: false);

        // Assert
        Assert.NotNull(task.Artifacts);
        Assert.Single(task.Artifacts);
    }

    [Fact]
    public void ApplyArtifactUpdate_UpdatesNameAndDescriptionOnAppend()
    {
        // Arrange
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };

        ArtifactHelper.ApplyArtifactUpdate(task, new Artifact
        {
            ArtifactId = "artifact1",
            Name = "Original Name",
            Description = "Original Description",
            Parts = [new TextPart { Text = "Content" }]
        }, append: false);

        // Act - append with new name, no description
        ArtifactHelper.ApplyArtifactUpdate(task, new Artifact
        {
            ArtifactId = "artifact1",
            Name = "Updated Name",
            Parts = [new TextPart { Text = " More" }]
        }, append: true);

        // Assert - name updated, description preserved
        Assert.Equal("Updated Name", task.Artifacts![0].Name);
        Assert.Equal("Original Description", task.Artifacts[0].Description);
    }

    [Fact]
    public void ApplyArtifactUpdate_CreatesDefensiveCopy()
    {
        // Arrange
        var task = new AgentTask { Id = "task1", Status = new AgentTaskStatus { State = TaskState.Submitted } };
        var originalParts = new List<Part> { new TextPart { Text = "Content" } };
        var artifact = new Artifact
        {
            ArtifactId = "artifact1",
            Parts = originalParts
        };

        // Act
        ArtifactHelper.ApplyArtifactUpdate(task, artifact, append: false);

        // Mutate the original list
        originalParts.Add(new TextPart { Text = "Injected" });

        // Assert - stored artifact should not be affected
        Assert.Single(task.Artifacts![0].Parts);
    }
}
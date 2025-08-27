using System;

namespace A2A.UnitTests.Server;

public class TaskManagerTests
{
    [Fact]
    public async Task SendMessageReturnsAMessage()
    {
        var taskManager = new TaskManager();
        var taskSendParams = new MessageSendParams
        {
            Message = new Message
            {
                Parts = [
                    new TextPart
                    {
                        Text = "Hello, World!"
                    }
                ]
            },
        };
        string messageReceived = string.Empty;
        taskManager.OnMessageReceived = (messageSendParams, _) =>
        {
            messageReceived = messageSendParams.Message.Parts.OfType<TextPart>().First().Text;
            return Task.FromResult(new Message
            {
                Parts = [
                    new TextPart
                    {
                        Text = "Goodbye, World!"
                    }
                ]
            });
        };
        var a2aResponse = await taskManager.SendMessageAsync(taskSendParams) as Message;
        Assert.NotNull(a2aResponse);
        Assert.Equal("Goodbye, World!", a2aResponse.Parts.OfType<TextPart>().First().Text);
        Assert.Equal("Hello, World!", messageReceived);
    }

    [Fact]
    public async Task CreateAndRetrieveTask()
    {
        var taskManager = new TaskManager();
        var messageSendParams = new MessageSendParams
        {
            Message = new Message
            {
                Parts = [
                    new TextPart
                    {
                        Text = "Hello, World!"
                    }
                ]
            },
        };
        var task = await taskManager.SendMessageAsync(messageSendParams) as AgentTask;
        Assert.NotNull(task);

        Assert.Equal(TaskState.Submitted, task.Status.State);

        var retrievedTask = await taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id });
        Assert.NotNull(retrievedTask);
        Assert.Equal(task.Id, retrievedTask.Id);
        Assert.Equal(TaskState.Submitted, retrievedTask.Status.State);
    }

    [Fact]
    public async Task CancelTask()
    {
        var taskManager = new TaskManager();
        var taskSendParams = new MessageSendParams
        {
            Message = new Message
            {
                Parts = [
                    new TextPart
                    {
                        Text = "Hello, World!"
                    }
                ]
            },
        };
        var task = await taskManager.SendMessageAsync(taskSendParams) as AgentTask;
        Assert.NotNull(task);
        Assert.Equal(TaskState.Submitted, task.Status.State);

        var cancelledTask = await taskManager.CancelTaskAsync(new TaskIdParams { Id = task.Id });
        Assert.NotNull(cancelledTask);
        Assert.Equal(task.Id, cancelledTask.Id);
        Assert.Equal(TaskState.Canceled, cancelledTask.Status.State);
    }

    [Fact]
    public async Task CancelTask_MoreThanOnce_Fails()
    {
        var taskManager = new TaskManager();
        var taskSendParams = new MessageSendParams
        {
            Message = new Message
            {
                Parts = [
                    new TextPart
                    {
                        Text = "Hello, World!"
                    }
                ]
            },
        };
        var task = await taskManager.SendMessageAsync(taskSendParams) as AgentTask;
        Assert.NotNull(task);
        Assert.Equal(TaskState.Submitted, task.Status.State);

        var cancelledTask = await taskManager.CancelTaskAsync(new TaskIdParams { Id = task.Id });
        Assert.NotNull(cancelledTask);
        Assert.Equal(task.Id, cancelledTask.Id);
        Assert.Equal(TaskState.Canceled, cancelledTask.Status.State);

        await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await taskManager.CancelTaskAsync(new TaskIdParams { Id = task.Id });
        });
    }

    [Fact]
    public async Task UpdateTask()
    {
        var taskManager = new TaskManager()
        {
            OnTaskUpdated = (task, _) =>
            {
                task.Status = task.Status with { State = TaskState.Working };
                return Task.CompletedTask;
            }
        };

        var taskSendParams = new MessageSendParams
        {
            Message = new Message
            {
                Parts = [
                    new TextPart
                    {
                        Text = "Hello, World!"
                    }
                ]
            },
        };
        var task = await taskManager.SendMessageAsync(taskSendParams) as AgentTask;
        Assert.NotNull(task);
        Assert.Equal(TaskState.Submitted, task.Status.State);

        var updateSendParams = new MessageSendParams
        {
            Message = new Message
            {
                TaskId = task.Id,
                Parts = [
                    new TextPart
                    {
                        Text = "Task updated!"
                    }
                ]
            },
        };
        var updatedTask = await taskManager.SendMessageAsync(updateSendParams) as AgentTask;
        Assert.NotNull(updatedTask);
        Assert.Equal(task.Id, updatedTask.Id);
        Assert.Equal(TaskState.Working, updatedTask.Status.State);
        Assert.NotNull(updatedTask.History);
        Assert.Equal("Task updated!", (updatedTask.History.Last().Parts[0] as TextPart)!.Text);
    }

    [Fact]
    public async Task UpdateTaskStatus()
    {
        var taskManager = new TaskManager();

        var taskSendParams = new MessageSendParams
        {
            Message = new Message
            {
                Parts = [
                    new TextPart
                    {
                        Text = "Hello, World!"
                    }
                ]
            },
        };
        var task = await taskManager.SendMessageAsync(taskSendParams) as AgentTask;
        Assert.NotNull(task);
        Assert.Equal(TaskState.Submitted, task.Status.State);

        await taskManager.UpdateStatusAsync(task.Id, TaskState.Completed, new Message
        {
            Parts = [
                    new TextPart
                    {
                        Text = "Task completed!"
                    }
                ]
        }
        );
        var completedTask = await taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id });
        Assert.NotNull(completedTask);
        Assert.Equal(task.Id, completedTask.Id);
        Assert.Equal(TaskState.Completed, completedTask.Status.State);
    }

    [Fact]
    public async Task ReturnArtifactSync()
    {
        var taskManager = new TaskManager();

        var taskSendParams = new MessageSendParams
        {
            Message = new Message
            {
                Parts = [
                    new TextPart
                    {
                        Text = "Write me a poem"
                    }
                ]
            },
        };
        var task = await taskManager.SendMessageAsync(taskSendParams) as AgentTask;
        Assert.NotNull(task);
        Assert.Equal(TaskState.Submitted, task.Status.State);

        var artifact = new Artifact
        {
            Name = "Test Artifact",
            Parts =
            [
                new TextPart
                {
                    Text = "When all at once, a host of golden daffodils,"
                }
            ]
        };
        await taskManager.ReturnArtifactAsync(task.Id, artifact);
        await taskManager.UpdateStatusAsync(task.Id, TaskState.Completed);
        var completedTask = await taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id });
        Assert.NotNull(completedTask);
        Assert.Equal(task.Id, completedTask.Id);
        Assert.Equal(TaskState.Completed, completedTask.Status.State);
        Assert.NotNull(completedTask.Artifacts);
        Assert.Single(completedTask.Artifacts);
        Assert.Equal("Test Artifact", completedTask.Artifacts[0].Name);
    }

    [Fact]
    public async Task CreateSendSubscribeTask()
    {
        var taskManager = new TaskManager();
        taskManager.OnTaskCreated = async (task, ct) =>
        {
            await taskManager.UpdateStatusAsync(task.Id, TaskState.Working, final: true, cancellationToken: ct);
        };

        var taskSendParams = new MessageSendParams
        {
            Message = new Message
            {
                Parts = [
                    new TextPart
                    {
                        Text = "Hello, World!"
                    }
                ]
            },
        };
        var taskEvents = taskManager.SendMessageStreamAsync(taskSendParams);
        var taskCount = 0;
        await foreach (var taskEvent in taskEvents)
        {
            taskCount++;
        }
        Assert.Equal(2, taskCount);
    }

    [Fact]
    public async Task EnsureTaskIsFirstReturnedEventFromMessageStream()
    {
        var taskManager = new TaskManager();
        taskManager.OnTaskCreated = async (task, ct) =>
        {
            await taskManager.UpdateStatusAsync(task.Id, TaskState.Working, final: true, cancellationToken: ct);
        };

        var taskSendParams = new MessageSendParams
        {
            Message = new Message
            {
                Parts = [
                    new TextPart
                    {
                        Text = "Hello, World!"
                    }
                ]
            },
        };
        var taskEvents = taskManager.SendMessageStreamAsync(taskSendParams);

        var isFirstEvent = true;
        await foreach (var taskEvent in taskEvents)
        {
            if (isFirstEvent)
            {
                Assert.NotNull(taskEvent);
                Assert.IsType<AgentTask>(taskEvent);
                isFirstEvent = false;
            }
        }
    }

    [Fact]
    public async Task VerifyTaskEventEnumerator()
    {
        var enumerator = new TaskUpdateEventEnumerator();

        var task = Task.Run(async () =>
        {
            await Task.Delay(1000);
            enumerator.NotifyEvent(new TaskStatusUpdateEvent
            {
                TaskId = "testTask",
                Status = new AgentTaskStatus
                {
                    State = TaskState.Working,
                    Timestamp = DateTime.UtcNow
                }
            });

            await Task.Delay(1000);
            enumerator.NotifyFinalEvent(new TaskStatusUpdateEvent
            {
                TaskId = "testTask",
                Status = new AgentTaskStatus
                {
                    State = TaskState.Completed,
                    Timestamp = DateTime.UtcNow
                }
            });
        });

        var eventCount = 0;
        await foreach (var taskEvent in enumerator)
        {
            Assert.NotNull(taskEvent);
            Assert.IsType<TaskStatusUpdateEvent>(taskEvent);
            eventCount++;
        }
        Assert.Equal(2, eventCount);
    }

    [Fact]
    public async Task SetPushNotificationAsync_SetsAndReturnsConfig()
    {
        // Arrange
        var sut = new TaskManager();
        var config = new TaskPushNotificationConfig
        {
            TaskId = "task-push-1",
            PushNotificationConfig = new PushNotificationConfig { Url = "http://callback" }
        };

        // Act
        var result = await sut.SetPushNotificationAsync(config);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("task-push-1", result.TaskId);
        Assert.Equal("http://callback", result.PushNotificationConfig.Url);
    }

    [Fact]
    public async Task SetPushNotificationAsync_ThrowsOnNullConfig()
    {
        // Arrange
        var sut = new TaskManager();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() => sut.SetPushNotificationAsync(null!));
        Assert.Equal(A2AErrorCode.InvalidParams, ex.ErrorCode);
    }

    [Fact]
    public async Task GetPushNotificationAsync_ReturnsConfig()
    {
        // Arrange
        var sut = new TaskManager();

        // Create the task first
        var task = await sut.CreateTaskAsync();

        var config = new TaskPushNotificationConfig
        {
            TaskId = task.Id,
            PushNotificationConfig = new PushNotificationConfig { Url = "http://callback2" }
        };
        await sut.SetPushNotificationAsync(config);

        // Act
        var result = await sut.GetPushNotificationAsync(new GetTaskPushNotificationConfigParams { Id = task.Id });

        // Assert
        Assert.NotNull(result);
        Assert.Equal(task.Id, result.TaskId);
        Assert.Equal("http://callback2", result.PushNotificationConfig.Url);
    }

    [Fact]
    public async Task GetPushNotificationAsync_ThrowsOnNullParams()
    {
        // Arrange
        var sut = new TaskManager();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() => sut.GetPushNotificationAsync(null!));
        Assert.Equal(A2AErrorCode.InvalidParams, ex.ErrorCode);
    }

    [Fact]
    public async Task SubscribeToTaskAsync_ReturnsEnumerator_WhenTaskExists()
    {
        // Arrange
        var sut = new TaskManager();
        var task = await sut.CreateTaskAsync();

        var sendParams = new MessageSendParams
        {
            Message = new Message
            {
                TaskId = task.Id,
                Parts = [new TextPart { Text = "init" }]
            }
        };

        var events = new List<A2AEvent>();
        var processorStarted = new TaskCompletionSource();

        var processor = Task.Run(async () =>
        {
            await foreach (var i in sut.SendMessageStreamAsync(sendParams))
            {
                events.Add(i);
                if (events.Count is 1)
                {
                    processorStarted.SetResult(); // Signal that processor is running and got the first event
                }

                if (events.Count is 3) break;
            }
        });

        // Wait for processor to start and receive the first event
        await processorStarted.Task;

        // Now post the updates
        await sut.UpdateStatusAsync(task.Id, TaskState.Working, new() { Parts = [new TextPart { Text = "second" }] });
        await sut.UpdateStatusAsync(task.Id, TaskState.Completed, new() { Parts = [new TextPart { Text = "done" }] }, final: true);

        await processor;

        Assert.Equal(3, events.Count);

        var init = Assert.IsType<AgentTask>(events[0]);
        Assert.Equal("init", init!.History![0].Parts[0].AsTextPart().Text);
        var t = Assert.IsType<TaskStatusUpdateEvent>(events[1]);
        Assert.Equal("second", t!.Status.Message!.Parts[0].AsTextPart().Text);
        t = Assert.IsType<TaskStatusUpdateEvent>(events[2]);
        Assert.Equal("done", t!.Status.Message!.Parts[0].AsTextPart().Text);
    }

    [Fact]
    public void SubscribeToTaskAsync_Throws_WhenTaskDoesNotExist()
    {
        // Arrange
        var sut = new TaskManager();

        // Act & Assert
        var ex = Assert.Throws<A2AException>(() => sut.SubscribeToTaskAsync(new TaskIdParams { Id = "notfound" }));
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.ErrorCode);
    }

    [Fact]
    public void SubscribeToTaskAsync_ThrowsOnNullParams()
    {
        // Arrange
        var sut = new TaskManager();

        // Act & Assert
        var ex = Assert.Throws<A2AException>(() => sut.SubscribeToTaskAsync(null!));
        Assert.Equal(A2AErrorCode.InvalidParams, ex.ErrorCode);
    }

    [Fact]
    public async Task GetPushNotificationAsync_ReturnsFirstConfig_WhenMultipleConfigsExistAndNoConfigIdSpecified()
    {
        // Arrange
        var sut = new TaskManager();
        var task = await sut.CreateTaskAsync();

        // Create multiple push notification configs for the same task
        var config1 = new TaskPushNotificationConfig
        {
            TaskId = task.Id,
            PushNotificationConfig = new PushNotificationConfig
            {
                Id = "config-id-1",
                Url = "http://first-config",
                Token = "token1"
            }
        };

        var config2 = new TaskPushNotificationConfig
        {
            TaskId = task.Id,
            PushNotificationConfig = new PushNotificationConfig
            {
                Id = "config-id-2",
                Url = "http://second-config",
                Token = "token2"
            }
        };

        var config3 = new TaskPushNotificationConfig
        {
            TaskId = task.Id,
            PushNotificationConfig = new PushNotificationConfig
            {
                Id = "config-id-3",
                Url = "http://third-config",
                Token = "token3"
            }
        };

        // Set all configs
        await sut.SetPushNotificationAsync(config1);
        await sut.SetPushNotificationAsync(config2);
        await sut.SetPushNotificationAsync(config3);

        // Act - Get push notification without specifying a config ID (should return first one)
        var result = await sut.GetPushNotificationAsync(new GetTaskPushNotificationConfigParams { Id = task.Id });

        // Assert - Should return the first config that was added
        Assert.NotNull(result);
        Assert.Equal(task.Id, result.TaskId);
        Assert.Equal("config-id-1", result.PushNotificationConfig.Id);
        Assert.Equal("http://first-config", result.PushNotificationConfig.Url);
        Assert.Equal("token1", result.PushNotificationConfig.Token);
    }

    [Fact]
    public async Task SendMessageAsync_RespectsHistoryLength()
    {
        var taskManager = new TaskManager();
        var taskSendParams = new MessageSendParams
        {
            Message = new Message
            {
                Parts = [new TextPart { Text = "First" }]
            },
        };
        // Create initial task
        var task = await taskManager.SendMessageAsync(taskSendParams) as AgentTask;
        Assert.NotNull(task);
        // Add more messages to history
        for (int i = 2; i <= 5; i++)
        {
            var updateParams = new MessageSendParams
            {
                Message = new Message { TaskId = task.Id, Parts = [new TextPart { Text = $"Msg{i}" }] },
            };
            await taskManager.SendMessageAsync(updateParams);
        }
        // Request with historyLength = 3
        var checkParams = new MessageSendParams
        {
            Message = new Message { TaskId = task.Id, Parts = [new TextPart { Text = "Check" }] },
            Configuration = new() { HistoryLength = 3 }
        };
        var resultTask = await taskManager.SendMessageAsync(checkParams) as AgentTask;
        Assert.NotNull(resultTask);
        Assert.NotNull(resultTask.History);
        Assert.Equal(3, resultTask.History.Count);
        Assert.Equal("Msg4", (resultTask.History[0].Parts[0] as TextPart)?.Text);
        Assert.Equal("Msg5", (resultTask.History[1].Parts[0] as TextPart)?.Text);
        Assert.Equal("Check", (resultTask.History[2].Parts[0] as TextPart)?.Text);
    }

    [Fact]
    public async Task CreateTaskAsync_ShouldThrowOperationCanceledException_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var taskManager = new TaskManager();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => taskManager.CreateTaskAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task CancelTaskAsync_ShouldThrowOperationCanceledException_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var taskManager = new TaskManager();
        var taskIdParams = new TaskIdParams { Id = "test-id" };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => taskManager.CancelTaskAsync(taskIdParams, cts.Token));
    }

    [Fact]
    public async Task GetTaskAsync_ShouldThrowOperationCanceledException_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var taskManager = new TaskManager();
        var taskQueryParams = new TaskQueryParams { Id = "test-id" };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => taskManager.GetTaskAsync(taskQueryParams, cts.Token));
    }

    [Fact]
    public async Task GetTaskAsync_ShouldNotCreateCopiesOfHistory_WhenTrimmed()
    {
        // Arrange
        var taskManager = new TaskManager();

        // Act & Assert
        var task = await taskManager.SendMessageAsync(new()
        {
            Message = { Parts = { new TextPart { Text = "hi" } } }
        }, default) as AgentTask;
        Assert.NotNull(task);

        task = await taskManager.SendMessageAsync(new()
        {
            Message = {
                TaskId = task.Id,
                Parts = { new TextPart { Text = "hi again" } },
            },
        }, default) as AgentTask;
        Assert.NotNull(task);

        var trimmedTask = await taskManager.GetTaskAsync(new() { HistoryLength = 1, Id = task.Id });
        Assert.NotNull(trimmedTask?.History);
        Assert.Single(trimmedTask.History);

        Assert.NotNull(task.History);
        Assert.Same(task.History[1], trimmedTask.History[0]);

        Assert.Equal(task.Status, trimmedTask.Status);
        Assert.Same(task.Status.Message, trimmedTask.Status.Message);
        Assert.Same(task.Id, trimmedTask.Id);
        Assert.Same(task.Metadata, trimmedTask.Metadata);
        Assert.Same(task.Artifacts, trimmedTask.Artifacts);
        Assert.Same(task.ContextId, trimmedTask.ContextId);

        var trimmedSentTask = await taskManager.SendMessageAsync(new()
        {
            Message = {
                TaskId = task.Id,
                Parts = { new TextPart { Text = "hi again 3" } },
            },
            Configuration = new()
            {
                HistoryLength = 1,
            },
        }, default) as AgentTask;
        Assert.NotNull(trimmedSentTask);
        Assert.NotNull(trimmedSentTask?.History);
        Assert.Single(trimmedSentTask.History);

        task = await taskManager.GetTaskAsync(new() { Id = task.Id });
        Assert.NotNull(task);

        Assert.NotNull(task.History);
        Assert.Same(task.History[2], trimmedSentTask.History[0]);

        Assert.Equal(task.Status, trimmedSentTask.Status);
        Assert.Same(task.Status.Message, trimmedSentTask.Status.Message);
        Assert.Same(task.Id, trimmedSentTask.Id);
        Assert.Same(task.Metadata, trimmedSentTask.Metadata);
        Assert.Same(task.Artifacts, trimmedSentTask.Artifacts);
        Assert.Same(task.ContextId, trimmedSentTask.ContextId);

        var shouldbeSameTask = await taskManager.GetTaskAsync(new() { Id = task.Id });
        Assert.NotNull(shouldbeSameTask);
        Assert.Same(task.History, shouldbeSameTask.History);
        Assert.Equal(task.Status, shouldbeSameTask.Status);
        Assert.Same(task.Status.Message, shouldbeSameTask.Status.Message);
        Assert.Same(task.Id, shouldbeSameTask.Id);
        Assert.Same(task.Metadata, shouldbeSameTask.Metadata);
        Assert.Same(task.Artifacts, shouldbeSameTask.Artifacts);
        Assert.Same(task.ContextId, shouldbeSameTask.ContextId);
    }

    [Fact]
    public async Task SendMessageAsync_ShouldThrowOperationCanceledException_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var taskManager = new TaskManager();
        var messageSendParams = new MessageSendParams();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => taskManager.SendMessageAsync(messageSendParams, cts.Token));
    }

    [Fact]
    public async Task SendMessageStreamAsync_ShouldThrowOperationCanceledException_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var taskManager = new TaskManager();
        var messageSendParams = new MessageSendParams();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => taskManager.SendMessageStreamAsync(messageSendParams, cts.Token).ToArrayAsync().AsTask());
    }

    [Fact]
    public void SubscribeToTaskAsync_ShouldThrowOperationCanceledException_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var taskManager = new TaskManager();
        var taskIdParams = new TaskIdParams { Id = "test-id" };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        Assert.Throws<OperationCanceledException>(() => taskManager.SubscribeToTaskAsync(taskIdParams, cts.Token));
    }

    [Fact]
    public async Task SetPushNotificationAsync_ShouldThrowOperationCanceledException_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var taskManager = new TaskManager();
        var pushNotificationConfig = new TaskPushNotificationConfig();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => taskManager.SetPushNotificationAsync(pushNotificationConfig, cts.Token));
    }

    [Fact]
    public async Task GetPushNotificationAsync_ShouldThrowOperationCanceledException_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var taskManager = new TaskManager();
        var notificationConfigParams = new GetTaskPushNotificationConfigParams { Id = "test-id" };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => taskManager.GetPushNotificationAsync(notificationConfigParams, cts.Token));
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldThrowOperationCanceledException_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var taskManager = new TaskManager();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => taskManager.UpdateStatusAsync("test-id", TaskState.Working, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ReturnArtifactAsync_ShouldThrowOperationCanceledException_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var taskManager = new TaskManager();
        var artifact = new Artifact();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => taskManager.ReturnArtifactAsync("test-id", artifact, cts.Token));
    }

    [Fact]
    public async Task SendMessageAsync_ShouldThrowA2AException_WhenTaskIdSpecifiedButTaskDoesNotExist()
    {
        // Arrange
        var taskManager = new TaskManager();
        var messageSendParams = new MessageSendParams
        {
            Message = new Message
            {
                TaskId = "non-existent-task-id",
                Parts = [new TextPart { Text = "Hello, World!" }]
            }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<A2AException>(() => taskManager.SendMessageAsync(messageSendParams));
        Assert.Equal(A2AErrorCode.TaskNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task SendMessageStreamAsync_ShouldThrowA2AException_WhenTaskIdSpecifiedButTaskDoesNotExist()
    {
        // Arrange
        var taskManager = new TaskManager();
        var messageSendParams = new MessageSendParams
        {
            Message = new Message
            {
                TaskId = "non-existent-task-id",
                Parts = [new TextPart { Text = "Hello, World!" }]
            }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<A2AException>(() => taskManager.SendMessageStreamAsync(messageSendParams).ToArrayAsync().AsTask());
        Assert.Equal(A2AErrorCode.TaskNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task SendMessageAsync_ShouldCreateNewTask_WhenNoTaskIdSpecified()
    {
        // Arrange
        var taskManager = new TaskManager();
        var messageSendParams = new MessageSendParams
        {
            Message = new Message
            {
                // No TaskId specified
                Parts = [new TextPart { Text = "Hello, World!" }]
            }
        };

        // Act
        var result = await taskManager.SendMessageAsync(messageSendParams);

        // Assert
        var task = Assert.IsType<AgentTask>(result);
        Assert.NotNull(task.Id);
        Assert.NotEmpty(task.Id);
    }

    [Fact]
    public async Task SendMessageStreamAsync_ShouldCreateNewTask_WhenNoTaskIdSpecified()
    {
        // Arrange
        var taskManager = new TaskManager();
        var messageSendParams = new MessageSendParams
        {
            Message = new Message
            {
                // No TaskId specified
                Parts = [new TextPart { Text = "Hello, World!" }]
            }
        };

        // Act
        var events = new List<A2AEvent>();
        await foreach (var evt in taskManager.SendMessageStreamAsync(messageSendParams))
        {
            events.Add(evt);
            break; // Just get the first event (which should be the task)
        }

        // Assert
        Assert.Single(events);
        var task = Assert.IsType<AgentTask>(events[0]);
        Assert.NotNull(task.Id);
        Assert.NotEmpty(task.Id);
    }

    [Fact]
    public async Task UpdateArtifactAsync_ShouldCreateNewArtifact_WhenAppendIsFalse()
    {
        // Arrange
        var taskManager = new TaskManager();
        var task = await taskManager.CreateTaskAsync();

        var artifact = new Artifact
        {
            ArtifactId = "test-artifact-1",
            Name = "Test Artifact 1",
            Parts = [new TextPart { Text = "First artifact content" }]
        };

        // Act
        await taskManager.UpdateArtifactAsync(task.Id, artifact, append: false);

        // Assert
        var retrievedTask = await taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id });
        Assert.NotNull(retrievedTask);
        Assert.NotNull(retrievedTask.Artifacts);
        Assert.Single(retrievedTask.Artifacts);
        Assert.Equal("test-artifact-1", retrievedTask.Artifacts[0].ArtifactId);
        Assert.Equal("Test Artifact 1", retrievedTask.Artifacts[0].Name);
        Assert.Single(retrievedTask.Artifacts[0].Parts);
        Assert.Equal("First artifact content", retrievedTask.Artifacts[0].Parts[0].AsTextPart().Text);
    }

    [Fact]
    public async Task UpdateArtifactAsync_ShouldCreateNewArtifact_WhenAppendIsTrueButNoExistingArtifacts()
    {
        // Arrange
        var taskManager = new TaskManager();
        var task = await taskManager.CreateTaskAsync();

        var artifact = new Artifact
        {
            ArtifactId = "test-artifact-1",
            Name = "Test Artifact 1",
            Parts = [new TextPart { Text = "First artifact content" }]
        };

        // Act
        await taskManager.UpdateArtifactAsync(task.Id, artifact, append: true);

        // Assert
        var retrievedTask = await taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id });
        Assert.NotNull(retrievedTask);
        Assert.NotNull(retrievedTask.Artifacts);
        Assert.Single(retrievedTask.Artifacts);
        Assert.Equal("test-artifact-1", retrievedTask.Artifacts[0].ArtifactId);
        Assert.Single(retrievedTask.Artifacts[0].Parts);
        Assert.Equal("First artifact content", retrievedTask.Artifacts[0].Parts[0].AsTextPart().Text);
    }

    [Fact]
    public async Task UpdateArtifactAsync_ShouldAppendToLastArtifact_WhenAppendIsTrue()
    {
        // Arrange
        var taskManager = new TaskManager();
        var task = await taskManager.CreateTaskAsync();

        // First artifact
        var firstArtifact = new Artifact
        {
            ArtifactId = "test-artifact-1",
            Name = "Test Artifact 1",
            Parts = [new TextPart { Text = "First part" }]
        };
        await taskManager.UpdateArtifactAsync(task.Id, firstArtifact, append: false);

        // Second artifact to append
        var secondArtifact = new Artifact
        {
            ArtifactId = "test-artifact-2",
            Name = "Test Artifact 2",
            Parts = [
                new TextPart { Text = "Second part" },
                new TextPart { Text = "Third part" }
            ]
        };

        // Act
        await taskManager.UpdateArtifactAsync(task.Id, secondArtifact, append: true);

        // Assert
        var retrievedTask = await taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id });
        Assert.NotNull(retrievedTask);
        Assert.NotNull(retrievedTask.Artifacts);
        Assert.Single(retrievedTask.Artifacts); // Still only one artifact

        var artifact = retrievedTask.Artifacts[0];
        Assert.Equal("test-artifact-1", artifact.ArtifactId); // Should keep the original artifact ID
        Assert.Equal("Test Artifact 1", artifact.Name); // Should keep the original name
        Assert.Equal(3, artifact.Parts.Count); // Should have all parts combined

        Assert.Equal("First part", artifact.Parts[0].AsTextPart().Text);
        Assert.Equal("Second part", artifact.Parts[1].AsTextPart().Text);
        Assert.Equal("Third part", artifact.Parts[2].AsTextPart().Text);
    }

    [Fact]
    public async Task UpdateArtifactAsync_ShouldAppendMultipleTimes_WhenAppendIsTrue()
    {
        // Arrange
        var taskManager = new TaskManager();
        var task = await taskManager.CreateTaskAsync();

        // Initial artifact
        var initialArtifact = new Artifact
        {
            ArtifactId = "base-artifact",
            Name = "Base Artifact",
            Parts = [new TextPart { Text = "Base content" }]
        };
        await taskManager.UpdateArtifactAsync(task.Id, initialArtifact, append: false);

        // Act - append multiple times
        var appendArtifact1 = new Artifact
        {
            Parts = [new TextPart { Text = "Append 1" }]
        };
        await taskManager.UpdateArtifactAsync(task.Id, appendArtifact1, append: true);

        var appendArtifact2 = new Artifact
        {
            Parts = [new TextPart { Text = "Append 2" }]
        };
        await taskManager.UpdateArtifactAsync(task.Id, appendArtifact2, append: true);

        // Assert
        var retrievedTask = await taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id });
        Assert.NotNull(retrievedTask);
        Assert.NotNull(retrievedTask.Artifacts);
        Assert.Single(retrievedTask.Artifacts);

        var artifact = retrievedTask.Artifacts[0];
        Assert.Equal(3, artifact.Parts.Count);
        Assert.Equal("Base content", artifact.Parts[0].AsTextPart().Text);
        Assert.Equal("Append 1", artifact.Parts[1].AsTextPart().Text);
        Assert.Equal("Append 2", artifact.Parts[2].AsTextPart().Text);
    }

    [Fact]
    public async Task UpdateArtifactAsync_ShouldNotifyWithCorrectAppendFlag()
    {
        // Arrange
        var taskManager = new TaskManager();
        var task = await taskManager.CreateTaskAsync();

        // Send initial message to ensure event stream is registered
        var sendParams = new MessageSendParams
        {
            Message = new Message
            {
                TaskId = task.Id,
                Parts = [new TextPart { Text = "init" }]
            }
        };
        await taskManager.SendMessageAsync(sendParams);

        var events = new List<TaskArtifactUpdateEvent>();
        var tcs = new TaskCompletionSource();
        using var mutex = new SemaphoreSlim(0, 1);

        // Capture events using the stream
        var eventProcessor = Task.Run(async () =>
        {
            await foreach (var evt in taskManager.SendMessageStreamAsync(sendParams))
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.SetResult();
                }

                if (evt is TaskArtifactUpdateEvent artifactEvent)
                {
                    events.Add(artifactEvent);
                    mutex.Release();
                }

                if (events.Count >= 3) break; // Wait for 3 artifact events
            }
        });

        await tcs.Task;

        // Act - Create initial artifact (append=false)
        var initialArtifact = new Artifact
        {
            ArtifactId = "test-artifact",
            Parts = [new TextPart { Text = "Initial" }]
        };
        await taskManager.UpdateArtifactAsync(task.Id, initialArtifact, append: false);
        await mutex.WaitAsync();

        // First event should have append=false
        Assert.False(events[0].Append);
        Assert.Equal("test-artifact", events[0].Artifact.ArtifactId);
        Assert.Single(events[0].Artifact.Parts);

        // Append to artifact (append=true)
        var appendArtifact1 = new Artifact
        {
            Parts = [new TextPart { Text = "Appended 1" }]
        };
        await taskManager.UpdateArtifactAsync(task.Id, appendArtifact1, append: true);
        await mutex.WaitAsync();

        // Second event should have append=true
        Assert.True(events[1].Append);
        Assert.Equal("test-artifact", events[1].Artifact.ArtifactId); // Same artifact ID
        Assert.Equal(2, events[1].Artifact.Parts.Count); // Combined parts

        // Add new artifact (append=false)
        var newArtifact = new Artifact
        {
            ArtifactId = "new-artifact",
            Parts = [new TextPart { Text = "New artifact" }]
        };
        await taskManager.UpdateArtifactAsync(task.Id, newArtifact, append: false);
        await mutex.WaitAsync();

        // Third event should have append=false (new artifact)
        Assert.False(events[2].Append);
        Assert.Equal("new-artifact", events[2].Artifact.ArtifactId);
        Assert.Single(events[2].Artifact.Parts);

        await eventProcessor;

        // Assert
        Assert.Equal(3, events.Count);
    }

    [Fact]
    public async Task UpdateArtifactAsync_ShouldSetLastChunk()
    {
        // Arrange
        var taskManager = new TaskManager();
        var task = await taskManager.CreateTaskAsync();
        var events = new List<TaskArtifactUpdateEvent>();

        var sendParams = new MessageSendParams
        {
            Message = new Message
            {
                TaskId = task.Id,
                Parts = [new TextPart { Text = "init" }]
            }
        };

        var eventProcessor = Task.Run(async () =>
        {
            await foreach (var evt in taskManager.SendMessageStreamAsync(sendParams))
            {
                if (evt is TaskArtifactUpdateEvent artifactEvent)
                {
                    events.Add(artifactEvent);
                }
                if (events.Count >= 1) break;
            }
        });

        await Task.Delay(100);

        // Act
        var artifact = new Artifact
        {
            ArtifactId = "test-artifact",
            Parts = [new TextPart { Text = "Final chunk" }]
        };
        await taskManager.UpdateArtifactAsync(task.Id, artifact, append: false, lastChunk: true);

        await eventProcessor;

        // Assert
        Assert.Single(events);
        Assert.True(events[0].LastChunk);
    }

    [Fact]
    public async Task UpdateArtifactAsync_ShouldThrowException_WhenTaskNotFound()
    {
        // Arrange
        var taskManager = new TaskManager();
        var artifact = new Artifact
        {
            ArtifactId = "test-artifact",
            Parts = [new TextPart { Text = "Test content" }]
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<A2AException>(() =>
            taskManager.UpdateArtifactAsync("non-existent-task", artifact));

        Assert.Equal(A2AErrorCode.TaskNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task UpdateArtifactAsync_ShouldThrowException_WhenTaskIdIsNull()
    {
        // Arrange
        var taskManager = new TaskManager();
        var artifact = new Artifact
        {
            ArtifactId = "test-artifact",
            Parts = [new TextPart { Text = "Test content" }]
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<A2AException>(() =>
            taskManager.UpdateArtifactAsync(null!, artifact));

        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
    }

    [Fact]
    public async Task UpdateArtifactAsync_ShouldThrowException_WhenArtifactIsNull()
    {
        // Arrange
        var taskManager = new TaskManager();
        var task = await taskManager.CreateTaskAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<A2AException>(() =>
            taskManager.UpdateArtifactAsync(task.Id, null!));

        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
    }

    [Fact]
    public async Task UpdateArtifactAsync_ShouldThrowOperationCanceledException_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var taskManager = new TaskManager();
        var artifact = new Artifact
        {
            ArtifactId = "test-artifact",
            Parts = [new TextPart { Text = "Test content" }]
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            taskManager.UpdateArtifactAsync("test-id", artifact, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task UpdateArtifactAsync_ShouldSetAppendFalse_WhenAppendTrueButNoExistingArtifacts()
    {
        // Arrange
        var taskManager = new TaskManager();
        var task = await taskManager.CreateTaskAsync();

        // Send initial message to ensure event stream is registered
        var sendParams = new MessageSendParams
        {
            Message = new Message
            {
                TaskId = task.Id,
                Parts = [new TextPart { Text = "init" }]
            }
        };
        await taskManager.SendMessageAsync(sendParams);

        var events = new List<TaskArtifactUpdateEvent>();
        var tcs = new TaskCompletionSource();

        // Capture events using the stream
        var eventProcessor = Task.Run(async () =>
        {
            await foreach (var evt in taskManager.SendMessageStreamAsync(sendParams))
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.SetResult();
                }

                if (evt is TaskArtifactUpdateEvent artifactEvent)
                {
                    events.Add(artifactEvent);
                    break; // Just capture the first artifact event
                }
            }
        });

        await tcs.Task; // Wait for event processor to start

        // Act - Call with append=true when no artifacts exist
        var artifact = new Artifact
        {
            ArtifactId = "test-artifact",
            Name = "Test Artifact",
            Parts = [new TextPart { Text = "First content" }]
        };
        await taskManager.UpdateArtifactAsync(task.Id, artifact, append: true);

        await eventProcessor;

        // Assert
        Assert.Single(events);
        Assert.False(events[0].Append); // Should be false because we created a new artifact, not appended
        Assert.Equal("test-artifact", events[0].Artifact.ArtifactId);
        Assert.Single(events[0].Artifact.Parts);

        // Verify that the artifact was actually created
        var retrievedTask = await taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id });
        Assert.NotNull(retrievedTask);
        Assert.NotNull(retrievedTask.Artifacts);
        Assert.Single(retrievedTask.Artifacts);
        Assert.Equal("test-artifact", retrievedTask.Artifacts[0].ArtifactId);
    }
}

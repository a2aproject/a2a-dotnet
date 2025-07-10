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
        taskManager.OnMessageReceived = (messageSendParams) =>
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

        var retrievedTask = await taskManager.GetTaskAsync(new TaskIdParams { Id = task.Id });
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
    public async Task UpdateTask()
    {
        var taskManager = new TaskManager()
        {
            OnTaskUpdated = (task) =>
            {
                task.Status.State = TaskState.Working;
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
        var completedTask = await taskManager.GetTaskAsync(new TaskIdParams { Id = task.Id });
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
        var completedTask = await taskManager.GetTaskAsync(new TaskIdParams { Id = task.Id });
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
        taskManager.OnTaskCreated = async (task) =>
        {
            await taskManager.UpdateStatusAsync(task.Id, TaskState.Working, final: true);
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
        var taskEvents = await taskManager.SendMessageStreamAsync(taskSendParams);
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
        taskManager.OnTaskCreated = async (task) =>
        {
            await taskManager.UpdateStatusAsync(task.Id, TaskState.Working, final: true);
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
        var taskEvents = await taskManager.SendMessageStreamAsync(taskSendParams);

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
        await Assert.ThrowsAsync<ArgumentException>(() => sut.SetPushNotificationAsync(null));
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
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.GetPushNotificationAsync(null));
    }

    [Fact]
    public async Task ResubscribeAsync_ReturnsEnumerator_WhenTaskExists()
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

        // Register the enumerator for the taskId
        var enumerator = await sut.SendMessageStreamAsync(sendParams);

        // Now, ResubscribeAsync should return the same enumerator instance for the taskId
        var result = sut.ResubscribeAsync(new TaskIdParams { Id = task.Id });
        Assert.Same(enumerator, result);
    }

    [Fact]
    public void ResubscribeAsync_Throws_WhenTaskDoesNotExist()
    {
        // Arrange
        var sut = new TaskManager();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => sut.ResubscribeAsync(new TaskIdParams { Id = "notfound" }));
    }

    [Fact]
    public void ResubscribeAsync_ThrowsOnNullParams()
    {
        // Arrange
        var sut = new TaskManager();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => sut.ResubscribeAsync(null));
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
}

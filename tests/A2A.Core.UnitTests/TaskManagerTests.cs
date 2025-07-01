namespace A2A.Core.UnitTests;

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
        Assert.Equal("Task updated!", (updatedTask.History.Last().Parts[0] as TextPart).Text);
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
            if(isFirstEvent)
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
}

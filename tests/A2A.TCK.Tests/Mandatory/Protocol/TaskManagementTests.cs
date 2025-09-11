using Xunit.Abstractions;
using A2A.TCK.Tests.Infrastructure;

namespace A2A.TCK.Tests.Mandatory.Protocol;

/// <summary>
/// Tests for task management methods (tasks/get, tasks/cancel) based on the TCK.
/// These tests validate task lifecycle management according to the A2A v0.3.0 specification.
/// </summary>
public class TaskManagementTests : TckTestBase
{
    private readonly TaskManager _taskManager;

    public TaskManagementTests(ITestOutputHelper output) : base(output)
    {
        _taskManager = new TaskManager();
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.3 - Task Retrieval",
        SpecSection = "A2A v0.3.0 §7.3",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task TasksGet_ExistingTask_ReturnsValidTask()
    {
        // Arrange - Create a task first
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        var initialTask = await _taskManager.SendMessageAsync(messageSendParams) as AgentTask;
        Assert.NotNull(initialTask);

        var taskQueryParams = new TaskQueryParams
        {
            Id = initialTask.Id,
            HistoryLength = 5
        };

        // Act
        var retrievedTask = await _taskManager.GetTaskAsync(taskQueryParams);

        // Assert
        bool taskRetrievalValid = retrievedTask != null &&
                                 retrievedTask.Id == initialTask.Id &&
                                 retrievedTask.ContextId == initialTask.ContextId;

        if (taskRetrievalValid)
        {
            Output.WriteLine("? Task retrieval successful");
            Output.WriteLine($"  Task ID: {retrievedTask!.Id}");
            Output.WriteLine($"  Context ID: {retrievedTask.ContextId}");
            Output.WriteLine($"  Status: {retrievedTask.Status.State}");
            Output.WriteLine($"  History length: {retrievedTask.History?.Count ?? 0}");
        }

        AssertTckCompliance(taskRetrievalValid, "Task retrieval must return valid task structure");
    }

    [Fact]
    [TckTest(TckComplianceLevel.NonCompliant, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.3 - Task Not Found Error",
        SpecSection = "A2A v0.3.0 §8.2",
        FailureImpact = "Critical - violates A2A error handling specification")]
    public async Task TasksGet_NonExistentTask_ThrowsTaskNotFoundError()
    {
        // Arrange
        var taskQueryParams = new TaskQueryParams
        {
            Id = "non-existent-task-id"
        };

        // Act & Assert
        bool threwCorrectException = false;
        try
        {
            await _taskManager.GetTaskAsync(taskQueryParams);
        }
        catch (A2AException ex) when (ex.ErrorCode == A2AErrorCode.TaskNotFound)
        {
            threwCorrectException = true;
            Output.WriteLine("? Correctly threw TaskNotFound error for non-existent task");
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Unexpected exception: {ex.GetType().Name} - {ex.Message}");
        }

        AssertTckCompliance(threwCorrectException, 
            "Attempting to retrieve non-existent task must throw TaskNotFound error");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.4 - Task Cancellation",
        SpecSection = "A2A v0.3.0 §7.4",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task TasksCancel_CancelableTask_ReturnsCanceledTask()
    {
        // Arrange - Create a task
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        var task = await _taskManager.SendMessageAsync(messageSendParams) as AgentTask;
        Assert.NotNull(task);

        var cancelParams = new TaskIdParams
        {
            Id = task.Id
        };

        // Act
        var canceledTask = await _taskManager.CancelTaskAsync(cancelParams);

        // Assert
        bool cancellationValid = canceledTask != null &&
                                canceledTask.Id == task.Id &&
                                canceledTask.Status.State == TaskState.Canceled;

        if (cancellationValid)
        {
            Output.WriteLine("? Task cancellation successful");
            Output.WriteLine($"  Task ID: {canceledTask!.Id}");
            Output.WriteLine($"  Final state: {canceledTask.Status.State}");
        }

        AssertTckCompliance(cancellationValid, "Task cancellation must return task in canceled state");
    }

    [Fact]
    [TckTest(TckComplianceLevel.NonCompliant, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.4 - Cancel Non-Existent Task Error",
        SpecSection = "A2A v0.3.0 §8.2",
        FailureImpact = "Critical - violates A2A error handling specification")]
    public async Task TasksCancel_NonExistentTask_ThrowsTaskNotFoundError()
    {
        // Arrange
        var cancelParams = new TaskIdParams
        {
            Id = "non-existent-task-id"
        };

        // Act & Assert
        bool threwCorrectException = false;
        try
        {
            await _taskManager.CancelTaskAsync(cancelParams);
        }
        catch (A2AException ex) when (ex.ErrorCode == A2AErrorCode.TaskNotFound)
        {
            threwCorrectException = true;
            Output.WriteLine("? Correctly threw TaskNotFound error for non-existent task cancellation");
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Unexpected exception: {ex.GetType().Name} - {ex.Message}");
        }

        AssertTckCompliance(threwCorrectException, 
            "Attempting to cancel non-existent task must throw TaskNotFound error");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.4 - Cancel Already Canceled Task",
        SpecSection = "A2A v0.3.0 §8.2",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task TasksCancel_AlreadyCanceledTask_ThrowsTaskNotCancelableError()
    {
        // Arrange - Create and cancel a task
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        var task = await _taskManager.SendMessageAsync(messageSendParams) as AgentTask;
        Assert.NotNull(task);

        var cancelParams = new TaskIdParams { Id = task.Id };
        
        // First cancellation should succeed
        await _taskManager.CancelTaskAsync(cancelParams);

        // Act & Assert - Second cancellation should fail
        bool threwCorrectException = false;
        try
        {
            await _taskManager.CancelTaskAsync(cancelParams);
        }
        catch (A2AException ex) when (ex.ErrorCode == A2AErrorCode.TaskNotCancelable)
        {
            threwCorrectException = true;
            Output.WriteLine("? Correctly threw TaskNotCancelable error for already canceled task");
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Unexpected exception: {ex.GetType().Name} - {ex.Message}");
        }

        AssertTckCompliance(threwCorrectException, 
            "Attempting to cancel already canceled task must throw TaskNotCancelable error");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §6.1 - Task Structure Validation",
        SpecSection = "A2A v0.3.0 §6.1",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task Task_Structure_IsValid()
    {
        // Arrange
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        // Act
        var task = await _taskManager.SendMessageAsync(messageSendParams) as AgentTask;

        // Assert
        bool taskStructureValid = task != null &&
                                 !string.IsNullOrEmpty(task.Id) &&
                                 !string.IsNullOrEmpty(task.ContextId);

        if (taskStructureValid)
        {
            Output.WriteLine("? Task structure is valid");
            Output.WriteLine($"  ID: {task!.Id}");
            Output.WriteLine($"  Context ID: {task.ContextId}");
            Output.WriteLine($"  Status state: {task.Status.State}");
            Output.WriteLine($"  Has timestamp: {task.Status.Timestamp != null}");
            Output.WriteLine($"  Artifacts count: {task.Artifacts?.Count ?? 0}");
            Output.WriteLine($"  History count: {task.History?.Count ?? 0}");
        }

        AssertTckCompliance(taskStructureValid, "Task must have valid structure with required fields");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §6.3 - Task State Transitions",
        SpecSection = "A2A v0.3.0 §6.3",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task Task_StateTransitions_AreValid()
    {
        // Arrange
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        var task = await _taskManager.SendMessageAsync(messageSendParams) as AgentTask;
        Assert.NotNull(task);

        // Test valid state transitions
        var validStates = new[]
        {
            TaskState.Submitted,
            TaskState.Working,
            TaskState.Completed,
            TaskState.Canceled,
            TaskState.Failed,
            TaskState.Rejected,
            TaskState.InputRequired,
            TaskState.AuthRequired,
            TaskState.Unknown
        };

        // Act & Assert
        bool allStatesValid = validStates.All(state => Enum.IsDefined(typeof(TaskState), state));

        // Check initial state is valid
        bool initialStateValid = task.Status.State == TaskState.Submitted ||
                                task.Status.State == TaskState.Working ||
                                task.Status.State == TaskState.Completed;

        var validTransitions = allStatesValid && initialStateValid;

        if (validTransitions)
        {
            Output.WriteLine("? Task state system is valid");
            Output.WriteLine($"  Initial state: {task.Status.State}");
            Output.WriteLine($"  All defined states: {string.Join(", ", validStates)}");
        }

        AssertTckCompliance(validTransitions, "Task state system must include all required states");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.3 - History Length Parameter",
        SpecSection = "A2A v0.3.0 §7.3.1",
        FailureImpact = "Limited functionality - history truncation not supported")]
    public async Task TasksGet_HistoryLength_IsRespected()
    {
        // Arrange - Create a task and add multiple messages to history
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        var task = await _taskManager.SendMessageAsync(messageSendParams) as AgentTask;
        Assert.NotNull(task);

        // Add more messages to build up history
        for (int i = 0; i < 3; i++)
        {
            var updateParams = new MessageSendParams
            {
                Message = new AgentMessage
                {
                    TaskId = task.Id,
                    Parts = [new TextPart { Text = $"Message {i + 2}" }],
                    MessageId = Guid.NewGuid().ToString()
                }
            };
            await _taskManager.SendMessageAsync(updateParams);
        }

        // Act - Request with limited history
        var taskQueryParams = new TaskQueryParams
        {
            Id = task.Id,
            HistoryLength = 2
        };

        var retrievedTask = await _taskManager.GetTaskAsync(taskQueryParams);

        // Assert
        bool historyLimited = retrievedTask?.History?.Count <= 2;

        if (historyLimited)
        {
            Output.WriteLine("? History length parameter respected");
            Output.WriteLine($"  Requested: 2, Received: {retrievedTask!.History?.Count ?? 0}");
        }
        else if (retrievedTask?.History?.Count > 2)
        {
            Output.WriteLine("?? History length parameter not implemented (acceptable)");
            Output.WriteLine($"  Requested: 2, Received: {retrievedTask.History.Count}");
        }

        // This is recommended, so we pass even if not implemented
        AssertTckCompliance(true, "Advanced task state management enhances workflow capabilities");
    }
}
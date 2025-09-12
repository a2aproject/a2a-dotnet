using A2A.Integration.Tests.Infrastructure;
using Xunit.Abstractions;

namespace A2A.Integration.Tests.Mandatory.Protocol;

/// <summary>
/// Tests for task state transitions based on the upstream TCK.
/// These tests validate the task lifecycle and state transition compliance.
/// </summary>
public class StateTransitionTests : TckTestBase
{
    public StateTransitionTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 �6.3 - Valid State Transitions",
        SpecSection = "A2A v0.3.0 �6.3",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task StateTransition_ValidTransitions_AreAllowed()
    {
        // Arrange
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        var stateHistory = new List<TaskState>();

        ConfigureTaskManager(onTaskCreated: async (task, ct) =>
        {
            stateHistory.Add(task.Status.State);

            // Simulate valid state transitions
            await _taskManager.UpdateStatusAsync(task.Id, TaskState.Working, cancellationToken: ct);
            stateHistory.Add(TaskState.Working);

            await Task.Delay(50, ct); // Simulate work

            await _taskManager.UpdateStatusAsync(task.Id, TaskState.Completed, final: true, cancellationToken: ct);
            stateHistory.Add(TaskState.Completed);
        });

        // Act
        var response = await SendMessageViaJsonRpcAsync(messageSendParams);

        // Assert
        bool validTransitions = stateHistory.Count >= 2 &&
                               stateHistory[0] is TaskState.Submitted &&
                               stateHistory.Contains(TaskState.Working) &&
                               stateHistory.Last() is TaskState.Completed;

        if (validTransitions)
        {
            Output.WriteLine("? Valid state transitions observed");
            Output.WriteLine($"  Transition sequence: {string.Join(" ? ", stateHistory)}");
        }
        else
        {
            Output.WriteLine($"?? Unexpected state transitions: {string.Join(" ? ", stateHistory)}");
        }

        AssertTckCompliance(validTransitions, "Task state transitions must follow valid sequences");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 �6.3 - Terminal State Handling",
        SpecSection = "A2A v0.3.0 �6.3",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task StateTransition_TerminalStates_PreventFurtherTransitions()
    {
        // Arrange - Create and complete a task
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        ConfigureTaskManager(onTaskCreated: async (task, ct) =>
        {
            await _taskManager.UpdateStatusAsync(task.Id, TaskState.Completed, final: true, cancellationToken: ct);
        });

        var createResponse = await SendMessageViaJsonRpcAsync(messageSendParams);
        var task = createResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(task);

        // Act - Try to send another message to the completed task
        var continuationParams = new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                TaskId = task.Id,
                Parts = [new TextPart { Text = "This should fail" }],
                MessageId = Guid.NewGuid().ToString()
            }
        };

        var continuationResponse = await SendMessageViaJsonRpcAsync(continuationParams);

        // Assert - Should fail because task is in terminal state
        bool terminalStateRespected = continuationResponse.Error is not null;

        if (terminalStateRespected)
        {
            Output.WriteLine("? Terminal state properly prevents further messages");
            Output.WriteLine($"  Error: {continuationResponse.Error!.Message}");
        }
        else
        {
            Output.WriteLine("?? Terminal state was not respected");
        }

        AssertTckCompliance(terminalStateRespected, "Terminal states must prevent further task modifications");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 �6.3 - All Required States Defined",
        SpecSection = "A2A v0.3.0 �6.3",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public void StateTransition_AllRequiredStates_AreDefined()
    {
        // Arrange - List all required states per A2A specification
        var requiredStates = new[]
        {
            TaskState.Submitted,
            TaskState.Working,
            TaskState.InputRequired,
            TaskState.Completed,
            TaskState.Canceled,
            TaskState.Failed,
            TaskState.Rejected,
            TaskState.AuthRequired,
            TaskState.Unknown
        };

        // Act & Assert - Verify all states are properly defined
        bool allStatesDefined = requiredStates.All(state => Enum.IsDefined(state));

        if (allStatesDefined)
        {
            Output.WriteLine("? All required task states are defined");
            Output.WriteLine($"  States: {string.Join(", ", requiredStates)}");
        }
        else
        {
            Output.WriteLine("? Some required task states are missing");
        }

        AssertTckCompliance(allStatesDefined, "All A2A v0.3.0 required task states must be defined");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 �6.3 - Input Required State Handling",
        SpecSection = "A2A v0.3.0 �6.3",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task StateTransition_InputRequiredState_PausesTaskExecution()
    {
        // Arrange - Create a task that requires input
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage("I need assistance with something")
        };

        ConfigureTaskManager(onTaskCreated: async (task, ct) =>
        {
            // Set task to input-required state
            await _taskManager.UpdateStatusAsync(task.Id, TaskState.InputRequired,
                new AgentMessage
                {
                    Role = MessageRole.Agent,
                    Parts = [new TextPart { Text = "What specific assistance do you need?" }],
                    MessageId = Guid.NewGuid().ToString()
                }, cancellationToken: ct);
        });

        // Act
        var response = await SendMessageViaJsonRpcAsync(messageSendParams);

        // Assert
        var task = response.Result?.Deserialize<AgentTask>();
        bool inputRequiredHandled = task is not null &&
                                   task.Status.State is TaskState.InputRequired &&
                                   task.Status.Message is not null;

        if (inputRequiredHandled)
        {
            Output.WriteLine("? Input-required state properly handled");
            Output.WriteLine($"  State: {task!.Status.State}");
            Output.WriteLine($"  Agent message: {task.Status.Message!.Parts[0].AsTextPart().Text}");
        }

        AssertTckCompliance(inputRequiredHandled, "Input-required state must pause task execution and provide agent message");
    }
}

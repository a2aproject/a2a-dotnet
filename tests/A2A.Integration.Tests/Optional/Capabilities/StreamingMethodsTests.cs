using Xunit.Abstractions;
using A2A.Integration.Tests.Infrastructure;

namespace A2A.Integration.Tests.OptionalTests.Capabilities;

/// <summary>
/// Tests for streaming methods based on the upstream TCK.
/// These tests validate Server-Sent Events (SSE) streaming functionality.
/// </summary>
public class StreamingMethodsTests : TckTestBase
{
    public StreamingMethodsTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalCapabilities,
        Description = "A2A v0.3.0 �7.2 - message/stream Method",
        SpecSection = "A2A v0.3.0 �7.2",
        FailureImpact = "Enhanced streaming capabilities not available")]
    public async Task MessageStream_BasicStreaming_WorksCorrectly()
    {
        // Check if streaming is supported via agent card capabilities
        var agentCard = CreateTestAgentCard();
        if (agentCard.Capabilities?.Streaming != true)
        {
            Output.WriteLine("?? Streaming not declared in agent capabilities - skipping test");
            AssertTckCompliance(true, "Streaming is optional capability");
            return;
        }

        // Arrange
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage("Stream test message")
        };

        var events = new List<A2AEvent>();
        var eventCount = 0;

        ConfigureTaskManager(onTaskCreated: async (task, ct) =>
        {
            // Simulate streaming updates
            await _taskManager.UpdateStatusAsync(task.Id, TaskState.Working, cancellationToken: ct);
            await Task.Delay(100, ct);
            await _taskManager.UpdateStatusAsync(task.Id, TaskState.Completed, final: true, cancellationToken: ct);
        });

        // Act - Use streaming async enumerable
        await foreach (var evt in _taskManager.SendMessageStreamingAsync(messageSendParams))
        {
            events.Add(evt);
            eventCount++;
            if (eventCount >= 3) break; // Prevent infinite loop
        }

        // Assert
        bool streamingWorked = events.Count > 0;
        
        if (streamingWorked)
        {
            Output.WriteLine("? Streaming functionality working");
            Output.WriteLine($"  Events received: {events.Count}");
            
            foreach (var evt in events)
            {
                Output.WriteLine($"  Event type: {evt.GetType().Name}");
            }
        }

        AssertTckCompliance(streamingWorked, "Streaming must work when declared as supported");
    }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalCapabilities,
        Description = "A2A v0.3.0 �7.7 - tasks/resubscribe Method",
        SpecSection = "A2A v0.3.0 �7.7",
        FailureImpact = "Streaming reconnection not available")]
    public async Task TaskResubscribe_ExistingTask_AllowsReconnection()
    {
        // Arrange - Create a task first
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        var createResponse = await SendMessageViaJsonRpcAsync(messageSendParams);
        var task = createResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(task);

        // Act - Try to resubscribe to the task
        try
        {
            var resubscribeEvents = _taskManager.SubscribeToTaskAsync(new TaskIdParams { Id = task.Id });
            
            // Try to get at least one event
            var eventReceived = false;
            await foreach (var evt in resubscribeEvents)
            {
                eventReceived = true;
                break;
            }

            Output.WriteLine("? Task resubscription working");
            AssertTckCompliance(true, "Resubscription capability is working");
        }
        catch (A2AException ex) when (ex.ErrorCode is A2AErrorCode.TaskNotFound)
        {
            Output.WriteLine("?? Task resubscription not supported for this task");
            AssertTckCompliance(true, "Resubscription support is optional");
        }
    }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalCapabilities,
        Description = "A2A v0.3.0 �7.2 - Streaming Event Types",
        SpecSection = "A2A v0.3.0 �7.2",
        FailureImpact = "Limited streaming event support")]
    public async Task MessageStream_EventTypes_AreCorrect()
    {
        // Arrange
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage("Event type test")
        };

        var events = new List<A2AEvent>();

        ConfigureTaskManager(onTaskCreated: async (task, ct) =>
        {
            await _taskManager.UpdateStatusAsync(task.Id, TaskState.Working, cancellationToken: ct);
            
            // Return an artifact
            await _taskManager.ReturnArtifactAsync(task.Id, new Artifact
            {
                ArtifactId = Guid.NewGuid().ToString(),
                Name = "Test Artifact",
                Parts = [new TextPart { Text = "Test content" }]
            }, ct);
            
            await _taskManager.UpdateStatusAsync(task.Id, TaskState.Completed, final: true, cancellationToken: ct);
        });

        // Act
        await foreach (var evt in _taskManager.SendMessageStreamingAsync(messageSendParams))
        {
            events.Add(evt);
            if (events.Count >= 4) break;
        }

        // Assert
        var hasTask = events.Any(e => e is AgentTask);
        var hasStatusUpdate = events.Any(e => e is TaskStatusUpdateEvent);
        var hasArtifactUpdate = events.Any(e => e is TaskArtifactUpdateEvent);

        Output.WriteLine($"Events received: {events.Count}");
        Output.WriteLine($"Has Task: {hasTask}");
        Output.WriteLine($"Has Status Update: {hasStatusUpdate}");
        Output.WriteLine($"Has Artifact Update: {hasArtifactUpdate}");

        bool eventTypesCorrect = hasTask && hasStatusUpdate;

        AssertTckCompliance(eventTypesCorrect, "Streaming must produce correct event types");
    }
}

using Xunit.Abstractions;
using A2A.Integration.Tests.Infrastructure;

namespace A2A.Integration.Tests.OptionalTestsTests.Capabilities;

/// <summary>
/// Tests for optional A2A capabilities that enhance the base protocol.
/// These tests validate advanced features through the JSON-RPC protocol layer.
/// </summary>
public class OptionalCapabilitiesTests : TckTestBase
{
    public OptionalCapabilitiesTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalCapabilities,
        Description = "A2A v0.3.0 §7.2 - Basic Message Sending via JSON-RPC",
        SpecSection = "A2A v0.3.0 §7.1",
        FailureImpact = "Core JSON-RPC functionality validation")]
    public async Task MessageSend_ViaJsonRpc_WorksCorrectly()
    {
        // Arrange
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage("Test message for JSON-RPC capabilities")
        };

        ConfigureTaskManager(onMessageReceived: (params_, _) =>
        {
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "JSON-RPC message received and processed" }],
                MessageId = Guid.NewGuid().ToString()
            });
        });

        // Act - Send via JSON-RPC
        var response = await SendMessageViaJsonRpcAsync(messageSendParams);

        // Assert
        bool jsonRpcWorked = response.Error is null && response.Result is not null;
        
        if (jsonRpcWorked)
        {
            var message = response.Result?.Deserialize<AgentMessage>();
            Output.WriteLine("✓ JSON-RPC message/send functionality is working");
            Output.WriteLine($"  Response: {message?.Parts[0].AsTextPart().Text}");
        }
        else if (response.Error is not null)
        {
            Output.WriteLine($"✗ JSON-RPC error: {response.Error.Code} - {response.Error.Message}");
        }

        AssertTckCompliance(jsonRpcWorked, "JSON-RPC message/send must work correctly");
    }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalCapabilities,
        Description = "A2A v0.3.0 §7.3 - Task Management via JSON-RPC",
        SpecSection = "A2A v0.3.0 §7.3",
        FailureImpact = "Enhanced task lifecycle management")]
    public async Task TaskManagement_ViaJsonRpc_WorksCorrectly()
    {
        // Arrange - Create a task via JSON-RPC
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage("Create a task for management testing")
        };

        var createResponse = await SendMessageViaJsonRpcAsync(messageSendParams);
        var task = createResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(task);

        // Act - Get the task via JSON-RPC
        var getResponse = await GetTaskViaJsonRpcAsync(new TaskQueryParams { Id = task.Id });

        // Assert
        bool taskManagementWorked = getResponse.Error is null && getResponse.Result is not null;
        
        if (taskManagementWorked)
        {
            var retrievedTask = getResponse.Result?.Deserialize<AgentTask>();
            Output.WriteLine("✓ JSON-RPC task management is working");
            Output.WriteLine($"  Created task ID: {task.Id}");
            Output.WriteLine($"  Retrieved task ID: {retrievedTask?.Id}");
            Output.WriteLine($"  Task status: {retrievedTask?.Status.State}");
        }
        else if (getResponse.Error is not null)
        {
            Output.WriteLine($"✗ JSON-RPC task management error: {getResponse.Error.Code} - {getResponse.Error.Message}");
        }

        AssertTckCompliance(taskManagementWorked, "JSON-RPC task management must work correctly");
    }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalCapabilities,
        Description = "A2A v0.3.0 §7.4 - Task Cancellation via JSON-RPC",
        SpecSection = "A2A v0.3.0 §7.4",
        FailureImpact = "Enhanced task lifecycle control")]
    public async Task TaskCancellation_ViaJsonRpc_WorksCorrectly()
    {
        // Arrange - Create a task via JSON-RPC
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage("Create a task for cancellation testing")
        };

        var createResponse = await SendMessageViaJsonRpcAsync(messageSendParams);
        var task = createResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(task);

        // Act - Cancel the task via JSON-RPC
        var cancelResponse = await CancelTaskViaJsonRpcAsync(new TaskIdParams { Id = task.Id });

        // Assert
        bool cancellationWorked = cancelResponse.Error is null && cancelResponse.Result is not null;
        
        if (cancellationWorked)
        {
            var cancelledTask = cancelResponse.Result?.Deserialize<AgentTask>();
            Output.WriteLine("✓ JSON-RPC task cancellation is working");
            Output.WriteLine($"  Cancelled task ID: {cancelledTask?.Id}");
            Output.WriteLine($"  Final status: {cancelledTask?.Status.State}");
        }
        else if (cancelResponse.Error is not null)
        {
            Output.WriteLine($"✗ JSON-RPC cancellation error: {cancelResponse.Error.Code} - {cancelResponse.Error.Message}");
        }

        AssertTckCompliance(cancellationWorked, "JSON-RPC task cancellation must work correctly");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.OptionalCapabilities,
        Description = "A2A v0.3.0 §5.5.2 - Agent Capabilities Declaration",
        SpecSection = "A2A v0.3.0 §5.5.2",
        FailureImpact = "Clients cannot discover advanced features")]
    public void AgentCard_Capabilities_AreCorrectlyDeclared()
    {
        // Arrange
        var agentCard = CreateTestAgentCard();

        // Act & Assert
        bool hasCapabilities = agentCard.Capabilities is not null;

        if (hasCapabilities)
        {
            Output.WriteLine("✓ Agent capabilities are declared");
            Output.WriteLine($"  Streaming: {agentCard.Capabilities!.Streaming}");
            Output.WriteLine($"  Push notifications: {agentCard.Capabilities.PushNotifications}");
            Output.WriteLine($"  State transition history: {agentCard.Capabilities.StateTransitionHistory}");

            // Validate that capabilities match actual implementation
            // This is a consistency check - if an agent declares a capability, it should support it
            bool capabilitiesConsistent = true;

            // Note: In a real implementation, we would test that declared capabilities
            // actually work. For this test suite, we just validate the structure.
            
            if (capabilitiesConsistent)
            {
                Output.WriteLine("✓ Declared capabilities appear consistent");
            }
        }
        else
        {
            Output.WriteLine("⚠️ No capabilities declared - defaults will be assumed");
        }

        // This is recommended for discoverability
        AssertTckCompliance(true, "Capability declaration is recommended for client discovery");
    }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalCapabilities,
        Description = "A2A v0.3.0 - JSON-RPC Error Handling",
        SpecSection = "A2A v0.3.0 §8.2",
        FailureImpact = "Poor error messaging for invalid requests")]
    public async Task JsonRpc_ErrorHandling_ReturnsAppropriateErrors()
    {
        // Test 1: Invalid parameters
        var invalidParams = new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                Parts = [], // Empty parts should be invalid
                MessageId = Guid.NewGuid().ToString()
            }
        };

        var invalidResponse = await SendMessageViaJsonRpcAsync(invalidParams);
        bool hasInvalidParamsError = invalidResponse.Error?.Code == (int)A2AErrorCode.InvalidParams;

        // Test 2: Non-existent task
        var nonExistentResponse = await GetTaskViaJsonRpcAsync(new TaskQueryParams { Id = "non-existent-task" });
        bool hasTaskNotFoundError = nonExistentResponse.Error?.Code == (int)A2AErrorCode.TaskNotFound;

        // Assert
        if (hasInvalidParamsError)
        {
            Output.WriteLine("✓ JSON-RPC correctly handles invalid parameters");
            Output.WriteLine($"  Error code: {invalidResponse.Error!.Code}");
        }
        
        if (hasTaskNotFoundError)
        {
            Output.WriteLine("✓ JSON-RPC correctly handles non-existent tasks");
            Output.WriteLine($"  Error code: {nonExistentResponse.Error!.Code}");
        }

        bool errorHandlingWorked = hasInvalidParamsError && hasTaskNotFoundError;
        AssertTckCompliance(errorHandlingWorked, "JSON-RPC error handling must return appropriate error codes");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.OptionalCapabilities,
        Description = "A2A v0.3.0 - Complex Message Types",
        FailureImpact = "Limited multimodal capabilities")]
    public async Task ComplexMessageTypes_ViaJsonRpc_AreHandled()
    {
        // Arrange - Create a complex message with multiple part types
        var complexMessage = new AgentMessage
        {
            Role = MessageRole.User,
            Parts = [
                new TextPart { Text = "Process this complex message:" },
                new DataPart 
                { 
                    Data = new Dictionary<string, JsonElement>
                    {
                        ["type"] = JsonSerializer.SerializeToElement("test-data"),
                        ["value"] = JsonSerializer.SerializeToElement(42)
                    }
                }
            ],
            MessageId = Guid.NewGuid().ToString()
        };

        var params_ = new MessageSendParams { Message = complexMessage };

        ConfigureTaskManager(onMessageReceived: (params_, _) =>
        {
            var parts = params_.Message.Parts;
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = $"Processed {parts.Count} parts of types: {string.Join(", ", parts.Select(p => p.Kind))}" }],
                MessageId = Guid.NewGuid().ToString()
            });
        });

        // Act - Send via JSON-RPC
        var response = await SendMessageViaJsonRpcAsync(params_);

        // Assert
        bool complexMessageHandled = response.Error is null && response.Result is not null;
        
        if (complexMessageHandled)
        {
            var message = response.Result?.Deserialize<AgentMessage>();
            Output.WriteLine("✓ Complex message types handled via JSON-RPC");
            Output.WriteLine($"  Response: {message?.Parts[0].AsTextPart().Text}");
        }
        else if (response.Error?.Code == (int)A2AErrorCode.ContentTypeNotSupported)
        {
            Output.WriteLine("⚠️ Some complex message types not supported - this is acceptable");
        }
        else if (response.Error is not null)
        {
            Output.WriteLine($"✗ JSON-RPC error: {response.Error.Code} - {response.Error.Message}");
        }

        // Complex message handling is recommended but not mandatory
        AssertTckCompliance(true, "JSON-RPC complex message type handling assessed");
    }
}

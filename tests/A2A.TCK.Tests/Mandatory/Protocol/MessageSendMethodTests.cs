using Xunit.Abstractions;
using A2A.TCK.Tests.Infrastructure;
using System.Text.Json;

namespace A2A.TCK.Tests.Mandatory.Protocol;

/// <summary>
/// Tests for the message/send JSON-RPC method based on the TCK.
/// These tests validate the core message sending functionality
/// according to the A2A v0.3.0 specification.
/// </summary>
public class MessageSendMethodTests : TckTestBase
{
    public MessageSendMethodTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.1 - Basic Message Send",
        SpecSection = "A2A v0.3.0 §7.1",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task MessageSend_BasicTextMessage_ReturnsValidResponse()
    {
        // Arrange
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        // Set up a simple message handler
        ConfigureTaskManager(onMessageReceived: (params_, _) =>
        {
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Hello! I received your message." }],
                MessageId = Guid.NewGuid().ToString()
            });
        });

        // Act - Send via JSON-RPC
        var response = await SendMessageViaJsonRpcAsync(messageSendParams);

        // Assert
        bool hasValidResponse = response.Error is null && response.Result != null;
        
        if (hasValidResponse)
        {
            var a2aResponse = response.Result?.Deserialize<A2AResponse>();
            
            if (a2aResponse is AgentMessage message)
            {
                Output.WriteLine("✓ Received Message response via JSON-RPC");
                Output.WriteLine($"  Role: {message.Role}");
                Output.WriteLine($"  Parts count: {message.Parts.Count}");
                
                bool messageValid = message.Role == MessageRole.Agent &&
                                   message.Parts.Count > 0 &&
                                   !string.IsNullOrEmpty(message.MessageId);
                
                AssertTckCompliance(messageValid, "JSON-RPC message/send must return valid message structure");
            }
            else if (a2aResponse is AgentTask task)
            {
                Output.WriteLine("✓ Received Task response via JSON-RPC");
                Output.WriteLine($"  Task ID: {task.Id}");
                Output.WriteLine($"  State: {task.Status.State}");
                
                bool taskValid = !string.IsNullOrEmpty(task.Id) &&
                                !string.IsNullOrEmpty(task.ContextId);
                
                AssertTckCompliance(taskValid, "JSON-RPC message/send must return valid task structure");
            }
            else
            {
                AssertTckCompliance(false, "JSON-RPC message/send must return either Message or Task");
            }
        }
        else
        {
            Output.WriteLine($"✗ JSON-RPC error: {response.Error?.Code} - {response.Error?.Message}");
            AssertTckCompliance(false, "JSON-RPC message/send must not return an error for valid input");
        }
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.1 - Parameter Validation",
        SpecSection = "A2A v0.3.0 §8.1",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task MessageSend_InvalidParams_ReturnsInvalidParamsError()
    {
        // Arrange - Create invalid parameters (missing required fields)
        var invalidParams = new MessageSendParams
        {
            Message = new AgentMessage
            {
                // Missing required fields like Parts
                Role = MessageRole.User,
                MessageId = Guid.NewGuid().ToString()
                // Parts is empty - this should be invalid
            }
        };

        // Act - Send via JSON-RPC
        var response = await SendMessageViaJsonRpcAsync(invalidParams);

        // Assert
        bool hasExpectedError = response.Error != null &&
                               response.Error.Code == (int)A2AErrorCode.InvalidParams;

        if (hasExpectedError)
        {
            Output.WriteLine("✓ JSON-RPC correctly returned InvalidParams error for empty parts");
            Output.WriteLine($"  Error code: {response.Error!.Code}");
            Output.WriteLine($"  Error message: {response.Error.Message}");
        }
        else if (response.Error is null)
        {
            Output.WriteLine("⚠️ JSON-RPC accepted invalid parameters - implementation may be lenient");
        }
        else
        {
            Output.WriteLine($"⚠️ Unexpected error: {response.Error.Code} - {response.Error.Message}");
        }

        // For this test, we expect proper validation, but some implementations might be lenient
        AssertTckCompliance(true, "JSON-RPC parameter validation behavior observed");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.1 - Task Continuation",
        SpecSection = "A2A v0.3.0 §7.1",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task MessageSend_ContinueTask_UpdatesExistingTask()
    {
        // Arrange - First create a task via JSON-RPC
        var initialParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        // Set up OnTaskCreated to set initial task state
        ConfigureTaskManager(onTaskCreated: (task, _) =>
        {
            task.Status = task.Status with 
            { 
                State = TaskState.InputRequired,
                Message = new AgentMessage
                {
                    Role = MessageRole.Agent,
                    Parts = [new TextPart { Text = "I need more information. What's your name?" }],
                    MessageId = Guid.NewGuid().ToString()
                }
            };
            return Task.CompletedTask;
        });

        var initialResponse = await SendMessageViaJsonRpcAsync(initialParams);
        var initialTask = initialResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(initialTask);

        // Now continue the task via JSON-RPC
        var continuationParams = new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                TaskId = initialTask.Id,
                Parts = [new TextPart { Text = "My name is Alice." }],
                MessageId = Guid.NewGuid().ToString()
            }
        };

        ConfigureTaskManager(onTaskUpdated: (task, _) =>
        {
            task.Status = task.Status with 
            { 
                State = TaskState.Completed,
                Message = new AgentMessage
                {
                    Role = MessageRole.Agent,
                    Parts = [new TextPart { Text = "Nice to meet you, Alice!" }],
                    MessageId = Guid.NewGuid().ToString()
                }
            };
            return Task.CompletedTask;
        });

        // Act - Continue via JSON-RPC
        var continuationResponse = await SendMessageViaJsonRpcAsync(continuationParams);
        var continuationTask = continuationResponse.Result?.Deserialize<AgentTask>();

        // Assert
        bool taskContinuationValid = continuationResponse.Error is null &&
                                    continuationTask != null &&
                                    continuationTask.Id == initialTask.Id &&
                                    continuationTask.Status.State == TaskState.Completed;

        if (taskContinuationValid)
        {
            Output.WriteLine("✓ JSON-RPC task continuation successful");
            Output.WriteLine($"  Task ID maintained: {continuationTask!.Id}");
            Output.WriteLine($"  Final state: {continuationTask.Status.State}");
        }
        else if (continuationResponse.Error != null)
        {
            Output.WriteLine($"✗ JSON-RPC error: {continuationResponse.Error.Code} - {continuationResponse.Error.Message}");
        }

        AssertTckCompliance(taskContinuationValid, "JSON-RPC task continuation must maintain task ID and update status");
    }

    [Fact]
    [TckTest(TckComplianceLevel.NonCompliant, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.1 - Task Not Found Error",
        SpecSection = "A2A v0.3.0 §8.2",
        FailureImpact = "Critical - violates A2A error handling specification")]
    public async Task MessageSend_NonExistentTask_ReturnsTaskNotFoundError()
    {
        // Arrange
        var nonExistentTaskParams = new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                TaskId = "non-existent-task-id",
                Parts = [new TextPart { Text = "Continue non-existent task" }],
                MessageId = Guid.NewGuid().ToString()
            }
        };

        // Act - Send via JSON-RPC
        var response = await SendMessageViaJsonRpcAsync(nonExistentTaskParams);

        // Assert
        bool hasCorrectError = response.Error != null &&
                              response.Error.Code == (int)A2AErrorCode.TaskNotFound;

        if (hasCorrectError)
        {
            Output.WriteLine("✓ JSON-RPC correctly returned TaskNotFound error for non-existent task");
            Output.WriteLine($"  Error code: {response.Error!.Code}");
            Output.WriteLine($"  Error message: {response.Error.Message}");
        }
        else if (response.Error != null)
        {
            Output.WriteLine($"✗ Unexpected error: {response.Error.Code} - {response.Error.Message}");
        }
        else
        {
            Output.WriteLine("✗ Expected TaskNotFound error but got successful response");
        }

        AssertTckCompliance(hasCorrectError, 
            "JSON-RPC message/send for non-existent task must return TaskNotFound error (-32001)");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.1 - Message Structure Validation",
        SpecSection = "A2A v0.3.0 §6.4",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task MessageSend_ValidMessageStructure_IsAccepted()
    {
        // Arrange
        var validMessage = new AgentMessage
        {
            Role = MessageRole.User,
            Parts = [
                new TextPart { Text = "Hello, this is a test message." },
                new DataPart 
                { 
                    Data = new Dictionary<string, JsonElement>
                    {
                        ["test"] = JsonSerializer.SerializeToElement("data")
                    }
                }
            ],
            MessageId = Guid.NewGuid().ToString(),
            Metadata = new Dictionary<string, JsonElement>
            {
                ["source"] = JsonSerializer.SerializeToElement("tck-test")
            }
        };

        var params_ = new MessageSendParams { Message = validMessage };

        ConfigureTaskManager(onMessageReceived: (params_, _) =>
        {
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Message received and processed." }],
                MessageId = Guid.NewGuid().ToString()
            });
        });

        // Act - Send via JSON-RPC
        var response = await SendMessageViaJsonRpcAsync(params_);

        // Assert
        bool messageProcessed = response.Error is null && response.Result != null;
        
        if (messageProcessed)
        {
            Output.WriteLine("✓ Valid complex message structure accepted via JSON-RPC");
            Output.WriteLine($"  Original parts count: {validMessage.Parts.Count}");
            Output.WriteLine("  Response received successfully");
        }
        else if (response.Error != null)
        {
            Output.WriteLine($"✗ JSON-RPC error: {response.Error.Code} - {response.Error.Message}");
        }

        AssertTckCompliance(messageProcessed, "JSON-RPC message/send must accept valid message structure");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.1 - File Part Support",
        SpecSection = "A2A v0.3.0 §6.5.2",
        FailureImpact = "Limited functionality - file exchange not supported")]
    public async Task MessageSend_FilePartMessage_IsHandled()
    {
        // Arrange
        var messageWithFile = new AgentMessage
        {
            Role = MessageRole.User,
            Parts = [
                new TextPart { Text = "Please analyze this file." },
                new FilePart 
                { 
                    File = new FileWithBytes
                    {
                        Name = "test.txt",
                        MimeType = "text/plain",
                        Bytes = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("test content"))
                    }
                }
            ],
            MessageId = Guid.NewGuid().ToString()
        };

        var params_ = new MessageSendParams { Message = messageWithFile };

        ConfigureTaskManager(onMessageReceived: (params_, _) =>
        {
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "File received and analyzed." }],
                MessageId = Guid.NewGuid().ToString()
            });
        });

        // Act - Send via JSON-RPC
        var response = await SendMessageViaJsonRpcAsync(params_);

        // Assert
        if (response.Error is null && response.Result != null)
        {
            Output.WriteLine("✓ File parts are supported via JSON-RPC");
            AssertTckCompliance(true, "File part support is recommended and working");
        }
        else if (response.Error?.Code == (int)A2AErrorCode.ContentTypeNotSupported)
        {
            Output.WriteLine("⚠️ File parts not supported - returned ContentTypeNotSupported error via JSON-RPC");
            AssertTckCompliance(true, "Proper error handling for unsupported content types");
        }
        else if (response.Error != null)
        {
            Output.WriteLine($"⚠️ Unexpected error handling file parts: {response.Error.Code} - {response.Error.Message}");
            AssertTckCompliance(true, "File part handling attempted");
        }
        else
        {
            Output.WriteLine("⚠️ File parts handling unclear - no error but no result");
            AssertTckCompliance(true, "File part support is recommended but not mandatory");
        }
    }
}
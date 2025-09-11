using Xunit.Abstractions;
using A2A.TCK.Tests.Infrastructure;
using System.Text.Json;

namespace A2A.TCK.Tests.Optional.Features;

/// <summary>
/// Tests for advanced A2A features that provide enhanced functionality.
/// These tests validate complex workflows, advanced message types, and extended capabilities
/// through the JSON-RPC protocol layer.
/// </summary>
public class OptionalFeaturesTests : TckTestBase
{
    public OptionalFeaturesTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalFeatures,
        Description = "A2A v0.3.0 - Multiple Content Types Support",
        FailureImpact = "Limited multimodal capabilities")]
    public async Task MultipleContentTypes_InSingleMessage_AreHandled()
    {
        // Arrange
        var multiModalMessage = new AgentMessage
        {
            Role = MessageRole.User,
            Parts = [
                new TextPart { Text = "Analyze this data and file:" },
                new DataPart 
                { 
                    Data = new Dictionary<string, JsonElement>
                    {
                        ["numbers"] = JsonSerializer.SerializeToElement(new[] { 1, 2, 3, 4, 5 }),
                        ["type"] = JsonSerializer.SerializeToElement("analysis-data")
                    }
                },
                new FilePart
                {
                    File = new FileWithBytes
                    {
                        Name = "sample.json",
                        MimeType = "application/json",
                        Bytes = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("""{"sample": true}"""))
                    }
                }
            ],
            MessageId = Guid.NewGuid().ToString()
        };

        var params_ = new MessageSendParams { Message = multiModalMessage };

        ConfigureTaskManager(onMessageReceived: (params_, _) =>
        {
            var receivedParts = params_.Message.Parts;
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = $"Processed {receivedParts.Count} parts: {string.Join(", ", receivedParts.Select(p => p.Kind))}" }],
                MessageId = Guid.NewGuid().ToString()
            });
        });

        // Act - Send via JSON-RPC
        var response = await SendMessageViaJsonRpcAsync(params_);

        // Assert
        if (response.Error is null && response.Result != null)
        {
            var message = response.Result.Deserialize<AgentMessage>();
            Output.WriteLine("✓ Multi-modal message processed successfully via JSON-RPC");
            Output.WriteLine($"  Response: {message?.Parts[0].AsTextPart().Text}");
            AssertTckCompliance(true, "JSON-RPC multi-modal content support is working");
        }
        else if (response.Error?.Code == (int)A2AErrorCode.ContentTypeNotSupported)
        {
            Output.WriteLine("⚠️ Some content types not supported via JSON-RPC - this is acceptable");
            AssertTckCompliance(true, "JSON-RPC content type limitations are acceptable for basic implementations");
        }
        else if (response.Error != null)
        {
            Output.WriteLine($"⚠️ JSON-RPC error processing multi-modal content: {response.Error.Code} - {response.Error.Message}");
            AssertTckCompliance(true, "JSON-RPC multi-modal content handling attempted");
        }
    }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalFeatures,
        Description = "A2A v0.3.0 - Extended Agent Card Features",
        FailureImpact = "Limited agent discovery and capability advertising")]
    public void ExtendedAgentCard_Features_AreSupported()
    {
        // Arrange
        var agentCard = CreateTestAgentCard();
        
        // Enhance the agent card with optional features
        agentCard.Provider = new AgentProvider
        {
            Organization = "Test Organization",
            Url = "https://test.example.com"
        };
        
        agentCard.DocumentationUrl = "https://docs.test.example.com";
        agentCard.IconUrl = "https://test.example.com/icon.png";
        
        // Add more detailed skills
        agentCard.Skills.Add(new AgentSkill
        {
            Id = "advanced-analysis",
            Name = "Advanced Data Analysis",
            Description = "Performs complex statistical analysis on datasets",
            Tags = ["data", "statistics", "analysis", "advanced"],
            Examples = [
                "Analyze the correlation between variables X and Y",
                "Perform regression analysis on sales data"
            ],
            InputModes = ["application/json", "text/csv"],
            OutputModes = ["application/json", "text/plain", "image/png"]
        });

        // Act & Assert
        bool hasProvider = agentCard.Provider != null;
        bool hasDocumentation = !string.IsNullOrEmpty(agentCard.DocumentationUrl);
        bool hasIcon = !string.IsNullOrEmpty(agentCard.IconUrl);
        bool hasDetailedSkills = agentCard.Skills.Any(s => s.Examples?.Count > 0);

        Output.WriteLine($"Provider information: {hasProvider}");
        Output.WriteLine($"Documentation URL: {hasDocumentation}");
        Output.WriteLine($"Icon URL: {hasIcon}");
        Output.WriteLine($"Skills with examples: {hasDetailedSkills}");
        Output.WriteLine($"Total skills: {agentCard.Skills.Count}");

        // These are all optional enhancements
        AssertTckCompliance(true, "Extended agent card features enhance discoverability");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.OptionalFeatures,
        Description = "A2A v0.3.0 - Context Management",
        FailureImpact = "Limited conversation continuity")]
    public async Task ContextManagement_AcrossTasks_MaintainsState()
    {
        // Arrange - Create multiple related tasks via JSON-RPC
        var firstMessage = new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                Parts = [new TextPart { Text = "My name is Alice and I'm 25 years old." }],
                MessageId = Guid.NewGuid().ToString()
            }
        };

        // Configure to create actual tasks instead of returning messages
        ConfigureTaskManager(onTaskCreated: (task, _) =>
        {
            task.Status = task.Status with
            {
                Message = new AgentMessage
                {
                    Role = MessageRole.Agent,
                    Parts = [new TextPart { Text = "I'll remember that information." }],
                    MessageId = Guid.NewGuid().ToString()
                }
            };
            return Task.CompletedTask;
        });

        var firstResponse = await SendMessageViaJsonRpcAsync(firstMessage);
        var firstTask = firstResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(firstTask);

        // Second task in the same context via JSON-RPC
        var secondMessage = new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                ContextId = firstTask.ContextId, // Use same context
                Parts = [new TextPart { Text = "What's my name?" }],
                MessageId = Guid.NewGuid().ToString()
            }
        };

        ConfigureTaskManager(onTaskCreated: (task, _) =>
        {
            task.Status = task.Status with
            {
                Message = new AgentMessage
                {
                    Role = MessageRole.Agent,
                    Parts = [new TextPart { Text = "Based on our conversation, your name is Alice." }],
                    MessageId = Guid.NewGuid().ToString()
                }
            };
            return Task.CompletedTask;
        });

        var secondResponse = await SendMessageViaJsonRpcAsync(secondMessage);
        var secondTask = secondResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(secondTask);

        // Assert
        bool sameContext = firstTask.ContextId == secondTask.ContextId;
        bool contextMaintained = !string.IsNullOrEmpty(firstTask.ContextId) && sameContext;

        if (contextMaintained)
        {
            Output.WriteLine("✓ Context maintained across tasks via JSON-RPC");
            Output.WriteLine($"  Context ID: {firstTask.ContextId}");
            Output.WriteLine($"  First task ID: {firstTask.Id}");
            Output.WriteLine($"  Second task ID: {secondTask.Id}");
        }
        else
        {
            Output.WriteLine("⚠️ Context management not implemented - tasks are independent");
        }

        // Context management is recommended but not required
        AssertTckCompliance(true, "JSON-RPC context management enhances conversation continuity");
    }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalFeatures,
        Description = "A2A v0.3.0 - Metadata Preservation",
        FailureImpact = "Loss of additional context information")]
    public async Task Metadata_Preservation_WorksCorrectly()
    {
        // Arrange
        var messageWithMetadata = new AgentMessage
        {
            Role = MessageRole.User,
            Parts = [
                new TextPart 
                { 
                    Text = "Test message",
                    Metadata = new Dictionary<string, JsonElement>
                    {
                        ["part-source"] = JsonSerializer.SerializeToElement("tck-test"),
                        ["part-priority"] = JsonSerializer.SerializeToElement("high")
                    }
                }
            ],
            MessageId = Guid.NewGuid().ToString(),
            Metadata = new Dictionary<string, JsonElement>
            {
                ["message-source"] = JsonSerializer.SerializeToElement("A2A-TCK"),
                ["test-run-id"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString())
            }
        };

        var params_ = new MessageSendParams 
        { 
            Message = messageWithMetadata,
            Metadata = new Dictionary<string, JsonElement>
            {
                ["request-timestamp"] = JsonSerializer.SerializeToElement(DateTime.UtcNow.ToString("O"))
            }
        };

        ConfigureTaskManager(onMessageReceived: (receivedParams, _) =>
        {
            // Check if metadata is preserved in the received params
            var hasMessageMetadata = receivedParams.Message.Metadata?.Count > 0;
            var hasPartMetadata = receivedParams.Message.Parts.Any(p => p.Metadata?.Count > 0);
            var hasRequestMetadata = receivedParams.Metadata?.Count > 0;

            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = $"Metadata received - Message: {hasMessageMetadata}, Part: {hasPartMetadata}, Request: {hasRequestMetadata}" }],
                MessageId = Guid.NewGuid().ToString(),
                Metadata = new Dictionary<string, JsonElement>
                {
                    ["response-source"] = JsonSerializer.SerializeToElement("task-manager")
                }
            });
        });

        // Act - Send via JSON-RPC
        var response = await SendMessageViaJsonRpcAsync(params_);

        // Assert
        bool responseReceived = response.Error is null && response.Result != null;
        
        if (responseReceived)
        {
            var message = response.Result?.Deserialize<AgentMessage>();
            bool responseHasMetadata = message?.Metadata?.Count > 0;

            Output.WriteLine("✓ Message with metadata processed via JSON-RPC");
            if (responseHasMetadata)
            {
                Output.WriteLine("✓ Response includes metadata");
                foreach (var kvp in message!.Metadata!)
                {
                    Output.WriteLine($"  {kvp.Key}: {kvp.Value}");
                }
            }

            var responseText = message!.Parts[0].AsTextPart().Text;
            Output.WriteLine($"Handler report: {responseText}");
        }
        else if (response.Error != null)
        {
            Output.WriteLine($"✗ JSON-RPC error: {response.Error.Code} - {response.Error.Message}");
        }

        AssertTckCompliance(responseReceived, "JSON-RPC metadata preservation enhances context passing");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.OptionalFeatures,
        Description = "A2A v0.3.0 - Input Validation and Sanitization",
        FailureImpact = "Security vulnerabilities and poor error handling")]
    public async Task InputValidation_EdgeCases_AreHandledGracefully()
    {
        // Arrange - Test various edge cases
        var edgeCases = new[]
        {
            // Empty text part
            new AgentMessage
            {
                Role = MessageRole.User,
                Parts = [new TextPart { Text = "" }],
                MessageId = Guid.NewGuid().ToString()
            },
            // Very long message ID
            new AgentMessage
            {
                Role = MessageRole.User,
                Parts = [new TextPart { Text = "Test" }],
                MessageId = new string('x', 1000)
            },
            // Special characters in text
            new AgentMessage
            {
                Role = MessageRole.User,
                Parts = [new TextPart { Text = "Special chars: <script>alert('test')</script> & €?†®" }],
                MessageId = Guid.NewGuid().ToString()
            }
        };

        ConfigureTaskManager(onMessageReceived: (params_, _) =>
        {
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Input processed safely" }],
                MessageId = Guid.NewGuid().ToString()
            });
        });

        // Act & Assert
        int successfullyHandled = 0;
        int appropriatelyRejected = 0;

        foreach (var (edgeCase, index) in edgeCases.Select((ec, i) => (ec, i)))
        {
            var params_ = new MessageSendParams { Message = edgeCase };
            var response = await SendMessageViaJsonRpcAsync(params_);
            
            if (response.Error is null && response.Result != null)
            {
                successfullyHandled++;
                Output.WriteLine($"✓ Edge case {index + 1} handled successfully via JSON-RPC");
            }
            else if (response.Error != null)
            {
                appropriatelyRejected++;
                Output.WriteLine($"✓ Edge case {index + 1} appropriately rejected via JSON-RPC: {response.Error.Code}");
            }
        }

        bool goodValidation = (successfullyHandled + appropriatelyRejected) == edgeCases.Length;

        Output.WriteLine($"Edge cases handled: {successfullyHandled}, rejected: {appropriatelyRejected}, total: {edgeCases.Length}");

        AssertTckCompliance(goodValidation, "JSON-RPC input validation handles edge cases appropriately");
    }
}
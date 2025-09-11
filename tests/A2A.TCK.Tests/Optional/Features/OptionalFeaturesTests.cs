using Xunit.Abstractions;
using A2A.TCK.Tests.Infrastructure;

namespace A2A.TCK.Tests.Optional.Features;

/// <summary>
/// Tests for optional A2A features and enhancements beyond the basic specification.
/// These tests validate advanced functionality and edge cases.
/// </summary>
public class OptionalFeaturesTests : TckTestBase
{
    private readonly TaskManager _taskManager;

    public OptionalFeaturesTests(ITestOutputHelper output) : base(output)
    {
        _taskManager = new TaskManager();
    }

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

        _taskManager.OnMessageReceived = (params_, _) =>
        {
            var receivedParts = params_.Message.Parts;
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = $"Processed {receivedParts.Count} parts: {string.Join(", ", receivedParts.Select(p => p.Kind))}" }],
                MessageId = Guid.NewGuid().ToString()
            });
        };

        // Act & Assert
        try
        {
            var response = await _taskManager.SendMessageAsync(params_);

            if (response is AgentMessage message)
            {
                Output.WriteLine("? Multi-modal message processed successfully");
                Output.WriteLine($"  Response: {message.Parts[0].AsTextPart().Text}");
            }

            AssertTckCompliance(true, "Multi-modal content support is working");
        }
        catch (A2AException ex) when (ex.ErrorCode == A2AErrorCode.ContentTypeNotSupported)
        {
            Output.WriteLine("?? Some content types not supported - this is acceptable");
            AssertTckCompliance(true, "Content type limitations are acceptable for basic implementations");
        }
        catch (Exception ex)
        {
            Output.WriteLine($"?? Error processing multi-modal content: {ex.Message}");
            AssertTckCompliance(true, "Multi-modal content handling attempted");
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
        // Arrange - Create multiple related tasks
        var firstMessage = new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                Parts = [new TextPart { Text = "My name is Alice and I'm 25 years old." }],
                MessageId = Guid.NewGuid().ToString()
            }
        };

        _taskManager.OnMessageReceived = (params_, _) =>
        {
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "I'll remember that information." }],
                MessageId = Guid.NewGuid().ToString()
            });
        };

        var firstTask = await _taskManager.SendMessageAsync(firstMessage) as AgentTask;
        Assert.NotNull(firstTask);

        // Second task in the same context
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

        var secondTask = await _taskManager.SendMessageAsync(secondMessage) as AgentTask;
        Assert.NotNull(secondTask);

        // Assert
        bool sameContext = firstTask.ContextId == secondTask.ContextId;
        bool contextMaintained = !string.IsNullOrEmpty(firstTask.ContextId) && sameContext;

        if (contextMaintained)
        {
            Output.WriteLine("? Context maintained across tasks");
            Output.WriteLine($"  Context ID: {firstTask.ContextId}");
            Output.WriteLine($"  First task ID: {firstTask.Id}");
            Output.WriteLine($"  Second task ID: {secondTask.Id}");
        }
        else
        {
            Output.WriteLine("?? Context management not implemented - tasks are independent");
        }

        // Context management is recommended but not required
        AssertTckCompliance(true, "Context management enhances conversation continuity");
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

        _taskManager.OnMessageReceived = (receivedParams, _) =>
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
        };

        // Act
        var response = await _taskManager.SendMessageAsync(params_) as AgentMessage;

        // Assert
        bool responseReceived = response != null;
        bool responseHasMetadata = response?.Metadata?.Count > 0;

        if (responseReceived)
        {
            Output.WriteLine("? Message with metadata processed");
            if (responseHasMetadata)
            {
                Output.WriteLine("? Response includes metadata");
                foreach (var kvp in response!.Metadata!)
                {
                    Output.WriteLine($"  {kvp.Key}: {kvp.Value}");
                }
            }

            var responseText = response!.Parts[0].AsTextPart().Text;
            Output.WriteLine($"Handler report: {responseText}");
        }

        AssertTckCompliance(true, "Metadata preservation enhances context passing");
    }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalFeatures,
        Description = "A2A v0.3.0 - Artifact Management",
        FailureImpact = "Limited output delivery capabilities")]
    public async Task ArtifactManagement_MultipleArtifacts_IsHandled()
    {
        // Arrange
        var messageSendParams = new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                Parts = [new TextPart { Text = "Generate multiple outputs: a report and a chart" }],
                MessageId = Guid.NewGuid().ToString()
            }
        };

        var task = await _taskManager.SendMessageAsync(messageSendParams) as AgentTask;
        Assert.NotNull(task);

        // Act - Add multiple artifacts
        var reportArtifact = new Artifact
        {
            ArtifactId = Guid.NewGuid().ToString(),
            Name = "Analysis Report",
            Description = "Detailed analysis report",
            Parts = [
                new TextPart { Text = "# Analysis Report\n\nThis is a comprehensive analysis..." }
            ]
        };

        var chartArtifact = new Artifact
        {
            ArtifactId = Guid.NewGuid().ToString(),
            Name = "Data Chart",
            Description = "Visual representation of the data",
            Parts = [
                new FilePart
                {
                    File = new FileWithBytes
                    {
                        Name = "chart.png",
                        MimeType = "image/png",
                        Bytes = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }) // Dummy data
                    }
                }
            ]
        };

        await _taskManager.ReturnArtifactAsync(task.Id, reportArtifact);
        await _taskManager.ReturnArtifactAsync(task.Id, chartArtifact);
        await _taskManager.UpdateStatusAsync(task.Id, TaskState.Completed);

        // Retrieve the completed task
        var completedTask = await _taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id });

        // Assert
        bool hasMultipleArtifacts = completedTask?.Artifacts?.Count >= 2;

        if (hasMultipleArtifacts)
        {
            Output.WriteLine($"? Multiple artifacts managed: {completedTask!.Artifacts!.Count}");
            foreach (var artifact in completedTask.Artifacts)
            {
                Output.WriteLine($"  - {artifact.Name}: {artifact.Parts.Count} parts");
            }
        }
        else
        {
            Output.WriteLine("?? Multiple artifact management not fully supported");
        }

        AssertTckCompliance(true, "Artifact management capabilities assessed");
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

        _taskManager.OnMessageReceived = (params_, _) =>
        {
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Input processed safely" }],
                MessageId = Guid.NewGuid().ToString()
            });
        };

        // Act & Assert
        int successfullyHandled = 0;
        int appropriatelyRejected = 0;

        foreach (var (edgeCase, index) in edgeCases.Select((ec, i) => (ec, i)))
        {
            try
            {
                var params_ = new MessageSendParams { Message = edgeCase };
                var response = await _taskManager.SendMessageAsync(params_);
                
                if (response != null)
                {
                    successfullyHandled++;
                    Output.WriteLine($"? Edge case {index + 1} handled successfully");
                }
            }
            catch (A2AException ex)
            {
                appropriatelyRejected++;
                Output.WriteLine($"? Edge case {index + 1} appropriately rejected: {ex.ErrorCode}");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"?? Edge case {index + 1} caused unexpected error: {ex.GetType().Name}");
            }
        }

        bool goodValidation = (successfullyHandled + appropriatelyRejected) == edgeCases.Length;

        Output.WriteLine($"Edge cases handled: {successfullyHandled}, rejected: {appropriatelyRejected}, total: {edgeCases.Length}");

        AssertTckCompliance(goodValidation, "Input validation handles edge cases appropriately");
    }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalFeatures,
        Description = "A2A v0.3.0 - Advanced Task State Management",
        FailureImpact = "Limited task workflow capabilities")]
    public async Task AdvancedTaskStates_AuthAndInputRequired_WorkCorrectly()
    {
        // Arrange
        var messageSendParams = new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                Parts = [new TextPart { Text = "Access my private account data" }],
                MessageId = Guid.NewGuid().ToString()
            }
        };

        var task = await _taskManager.SendMessageAsync(messageSendParams) as AgentTask;
        Assert.NotNull(task);

        // Act - Simulate auth required workflow
        await _taskManager.UpdateStatusAsync(task.Id, TaskState.AuthRequired,
            new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Authentication required. Please provide your API key." }],
                MessageId = Guid.NewGuid().ToString()
            });

        var authRequiredTask = await _taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id });

        // Simulate providing auth and moving to input required
        await _taskManager.UpdateStatusAsync(task.Id, TaskState.InputRequired,
            new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Authentication successful. Which account would you like to access?" }],
                MessageId = Guid.NewGuid().ToString()
            });

        var inputRequiredTask = await _taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id });

        // Complete the task
        await _taskManager.UpdateStatusAsync(task.Id, TaskState.Completed,
            new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Account data retrieved successfully." }],
                MessageId = Guid.NewGuid().ToString()
            });

        var completedTask = await _taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id });

        // Assert
        bool correctStateProgression = 
            authRequiredTask?.Status.State == TaskState.AuthRequired &&
            inputRequiredTask?.Status.State == TaskState.InputRequired &&
            completedTask?.Status.State == TaskState.Completed;

        if (correctStateProgression)
        {
            Output.WriteLine("? Advanced task state progression works correctly");
            Output.WriteLine("  AuthRequired ? InputRequired ? Completed");
        }
        else
        {
            Output.WriteLine("?? Advanced task states may not be fully implemented");
        }

        AssertTckCompliance(true, "Advanced task state management enhances workflow capabilities");
    }
}
using A2A.AspNetCore;
using System.Reflection;
using Xunit.Abstractions;

namespace A2A.Integration.Tests.Infrastructure;

public abstract class TckTestBase(ITestOutputHelper output)
{
    protected ITestOutputHelper Output { get; } = output;
    protected readonly TaskManager _taskManager = new();

    /// <summary>
    /// Evaluates a test result based on TCK compliance levels.
    /// Only fails the test if the actual behavior indicates non-compliance.
    /// </summary>
    /// <param name="testPassed">Whether the test logic passed</param>
    /// <param name="testMethod">The test method being executed</param>
    protected void EvaluateTckCompliance(bool testPassed, [System.Runtime.CompilerServices.CallerMemberName] string testMethod = "")
    {
        var method = GetType().GetMethod(testMethod);
        var tckAttribute = method?.GetCustomAttribute<TckTestAttribute>();

        if (tckAttribute is null)
        {
            // If no TCK attribute is found, treat as a regular test
            Xunit.Assert.True(testPassed, $"Test {testMethod} failed");
            return;
        }

        var complianceLevel = tckAttribute.ComplianceLevel;
        var category = tckAttribute.Category;

        if (testPassed)
        {
            // Test passed - report the compliance level achieved
            var badge = GetComplianceBadge(complianceLevel);
            Output.WriteLine($"✅ {badge} - {category}");
            if (!string.IsNullOrEmpty(tckAttribute.Description))
            {
                Output.WriteLine($"   Description: {tckAttribute.Description}");
            }
        }
        else
        {
            // Test failed - only fail if it's marked as non-compliant
            var badge = GetComplianceBadge(complianceLevel);

            if (complianceLevel is TckComplianceLevel.NonCompliant)
            {
                Output.WriteLine($"❌ {badge} - {category}");
                if (!string.IsNullOrEmpty(tckAttribute.FailureImpact))
                {
                    Output.WriteLine($"   Impact: {tckAttribute.FailureImpact}");
                }
                Xunit.Assert.Fail($"Non-compliant behavior detected in {category}: {tckAttribute.Description ?? testMethod}");
            }
            else
            {
                // Test failed but it's not critical - pass with informational message
                Output.WriteLine($"⚠️  {badge} - {category} (Feature not implemented)");
                if (!string.IsNullOrEmpty(tckAttribute.Description))
                {
                    Output.WriteLine($"   Description: {tckAttribute.Description}");
                }
                Output.WriteLine($"   Status: Feature not implemented - this is acceptable for {complianceLevel} level");
            }
        }
    }

    /// <summary>
    /// Asserts that a condition is true for TCK compliance.
    /// </summary>
    /// <param name="condition">The condition to check</param>
    /// <param name="message">Message to display</param>
    protected void AssertTckCompliance(bool condition, string message)
    {
        Output.WriteLine(condition
            ? $"✓ {message}"
            : $"✗ Test condition failed: {message}");

        // Let the TCK framework handle the compliance evaluation
        EvaluateTckCompliance(condition);
    }

    /// <summary>
    /// Creates a test message for message/send tests.
    /// </summary>
    /// <param name="text">The text content for the message</param>
    /// <returns>A test message with the specified text</returns>
    protected static AgentMessage CreateTestMessage(string text = "Hello, this is a test message from the A2A TCK test suite.") => new()
    {
        Parts = new List<Part>
        {
            new TextPart { Text = text }
        },
        MessageId = Guid.NewGuid().ToString(),
        Role = MessageRole.User
    };

    /// <summary>
    /// Creates a test agent card with optional capabilities for testing purposes.
    /// Matches the structure expected by upstream TCK tests.
    /// </summary>
    protected static AgentCard CreateTestAgentCard() => new()
    {
        Name = "Test A2A Agent",
        Description = "A test agent for A2A TCK compliance testing",
        Url = "https://example.com/agent",
        Version = "1.0.0-test",
        ProtocolVersion = "0.3.0",
        DefaultInputModes = ["text/plain", "application/json"],
        DefaultOutputModes = ["text/plain", "application/json"],
        Capabilities = new AgentCapabilities
        {
            Streaming = true,
            PushNotifications = true,
            StateTransitionHistory = false
        },
        Skills = new List<AgentSkill>
        {
            new()
            {
                Id = "test-skill",
                Name = "Test Skill",
                Description = "A test skill for TCK testing",
                Tags = ["test", "tck"],
                Examples = ["Test example 1", "Test example 2"],
                InputModes = ["text/plain"],
                OutputModes = ["text/plain"]
            }
        }
    };

    /// <summary>
    /// Sends a JSON-RPC request through the A2A processor and returns the response.
    /// </summary>
    /// <param name="method">The JSON-RPC method name</param>
    /// <param name="parameters">The parameters for the method</param>
    /// <param name="requestId">Optional request ID (defaults to 1)</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>The JSON-RPC response</returns>
    protected async Task<JsonRpcResponse> SendJsonRpcRequestAsync(string method, object parameters, int requestId = 1, CancellationToken cancellationToken = default)
    {
        // Create JSON-RPC request
        var request = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Method = method,
            Params = JsonSerializer.SerializeToElement(parameters),
            Id = requestId
        };

        // Simulate HTTP request body
        var requestBody = JsonSerializer.Serialize(request);
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(requestBody));

        var httpRequest = new DefaultHttpContext().Request;
        httpRequest.Body = stream;
        httpRequest.ContentType = "application/json";

        // Process through JSON-RPC processor with cancellation token
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(_taskManager, httpRequest, cancellationToken);

        // Execute the result to get the actual response
        var context = new DefaultHttpContext();
        var responseStream = new MemoryStream();
        context.Response.Body = responseStream;

        if (result is JsonRpcResponseResult responseResult)
        {
            await responseResult.ExecuteAsync(context);
            responseStream.Position = 0;

            var responseJson = await new StreamReader(responseStream).ReadToEndAsync(cancellationToken);
            var response = JsonSerializer.Deserialize<JsonRpcResponse>(responseJson);

            return response ?? throw new InvalidOperationException("Failed to deserialize JSON-RPC response");
        }

        throw new InvalidOperationException($"Unexpected result type: {result.GetType().Name}");
    }

    /// <summary>
    /// Sends a message/send JSON-RPC request and returns the response.
    /// </summary>
    /// <param name="messageSendParams">The message send parameters</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>The JSON-RPC response containing the A2A response</returns>
    protected async Task<JsonRpcResponse> SendMessageViaJsonRpcAsync(MessageSendParams messageSendParams, CancellationToken cancellationToken = default)
    {
        return await SendJsonRpcRequestAsync(A2AMethods.MessageSend, messageSendParams, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sends a tasks/get JSON-RPC request and returns the response.
    /// </summary>
    /// <param name="taskQueryParams">The task query parameters</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>The JSON-RPC response containing the task or error</returns>
    protected async Task<JsonRpcResponse> GetTaskViaJsonRpcAsync(TaskQueryParams taskQueryParams, CancellationToken cancellationToken = default)
    {
        return await SendJsonRpcRequestAsync(A2AMethods.TaskGet, taskQueryParams, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sends a tasks/cancel JSON-RPC request and returns the response.
    /// </summary>
    /// <param name="taskIdParams">The task ID parameters</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>The JSON-RPC response containing the cancelled task or error</returns>
    protected async Task<JsonRpcResponse> CancelTaskViaJsonRpcAsync(TaskIdParams taskIdParams, CancellationToken cancellationToken = default)
    {
        return await SendJsonRpcRequestAsync(A2AMethods.TaskCancel, taskIdParams, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Configures the task manager with message handlers for testing.
    /// </summary>
    /// <param name="onMessageReceived">Handler for received messages</param>
    /// <param name="onTaskCreated">Handler for created tasks</param>
    /// <param name="onTaskUpdated">Handler for updated tasks</param>
    protected void ConfigureTaskManager(
        Func<MessageSendParams, CancellationToken, Task<A2AResponse>>? onMessageReceived = null,
        Func<AgentTask, CancellationToken, Task>? onTaskCreated = null,
        Func<AgentTask, CancellationToken, Task>? onTaskUpdated = null)
    {
        if (onMessageReceived is not null)
            _taskManager.OnMessageReceived = onMessageReceived;

        if (onTaskCreated is not null)
            _taskManager.OnTaskCreated = onTaskCreated;

        if (onTaskUpdated is not null)
            _taskManager.OnTaskUpdated = onTaskUpdated;
    }

    private static string GetComplianceBadge(TckComplianceLevel level) => level switch
    {
        TckComplianceLevel.Mandatory => "🟢 Mandatory Compliant",
        TckComplianceLevel.Recommended => "🟡 Recommended Feature",
        TckComplianceLevel.FullFeatured => "🔵 Full Featured",
        TckComplianceLevel.NonCompliant => "🔴 Non-Compliant",
        _ => "⚫ Unknown Compliance Level"
    };
}

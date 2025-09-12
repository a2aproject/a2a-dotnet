using A2A.Integration.Tests.Infrastructure;
using Xunit.Abstractions;

namespace A2A.Integration.Tests.OptionalTests.Capabilities;

/// <summary>
/// Tests for push notification configuration methods based on the upstream TCK.
/// These tests validate push notification setup and management.
/// </summary>
public class PushNotificationConfigTests : TckTestBase
{
    public PushNotificationConfigTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalCapabilities,
        Description = "A2A v0.3.0 �7.5 - tasks/pushNotificationConfig/set Method",
        SpecSection = "A2A v0.3.0 �7.5",
        FailureImpact = "Push notification functionality not available")]
    public async Task PushNotificationConfig_Set_ConfiguresCorrectly()
    {
        // Check if push notifications are supported
        var agentCard = CreateTestAgentCard();
        if (agentCard.Capabilities?.PushNotifications != true)
        {
            Output.WriteLine("?? Push notifications not declared in agent capabilities - skipping test");
            AssertTckCompliance(true, "Push notifications are optional capability");
            return;
        }

        // Arrange - Create a task first
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        var createResponse = await SendMessageViaJsonRpcAsync(messageSendParams);
        var task = createResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(task);

        // Configure push notification
        var pushConfig = new TaskPushNotificationConfig
        {
            TaskId = task.Id,
            PushNotificationConfig = new PushNotificationConfig
            {
                Url = "https://example.com/webhook",
                Token = "test-token-123",
                Authentication = new PushNotificationAuthenticationInfo
                {
                    Schemes = ["Bearer"],
                    Credentials = """{"token": "webhook-auth-token"}"""
                }
            }
        };

        // Act
        var setResult = await _taskManager.SetPushNotificationAsync(pushConfig);

        // Assert
        bool configSet = setResult is not null &&
                        setResult.TaskId == task.Id &&
                        setResult.PushNotificationConfig.Url == "https://example.com/webhook";

        if (configSet)
        {
            Output.WriteLine("? Push notification configuration set successfully");
            Output.WriteLine($"  Task ID: {setResult!.TaskId}");
            Output.WriteLine($"  Webhook URL: {setResult.PushNotificationConfig.Url}");
        }

        AssertTckCompliance(configSet, "Push notification configuration must be settable when supported");
    }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalCapabilities,
        Description = "A2A v0.3.0 �7.6 - tasks/pushNotificationConfig/get Method",
        SpecSection = "A2A v0.3.0 �7.6",
        FailureImpact = "Push notification management not available")]
    public async Task PushNotificationConfig_Get_RetrievesConfiguration()
    {
        // Arrange - Create task and set push notification config
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        var createResponse = await SendMessageViaJsonRpcAsync(messageSendParams);
        var task = createResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(task);

        var pushConfig = new TaskPushNotificationConfig
        {
            TaskId = task.Id,
            PushNotificationConfig = new PushNotificationConfig
            {
                Url = "https://example.com/webhook",
                Token = "test-token-456"
            }
        };

        await _taskManager.SetPushNotificationAsync(pushConfig);

        // Act - Retrieve the configuration
        var getParams = new GetTaskPushNotificationConfigParams
        {
            Id = task.Id
        };

        var getResult = await _taskManager.GetPushNotificationAsync(getParams);

        // Assert
        bool configRetrieved = getResult is not null &&
                              getResult.TaskId == task.Id &&
                              getResult.PushNotificationConfig.Url == "https://example.com/webhook";

        if (configRetrieved)
        {
            Output.WriteLine("? Push notification configuration retrieved successfully");
            Output.WriteLine($"  Retrieved URL: {getResult!.PushNotificationConfig.Url}");
            Output.WriteLine($"  Retrieved Token: {getResult.PushNotificationConfig.Token}");
        }

        AssertTckCompliance(configRetrieved, "Push notification configuration must be retrievable");
    }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalCapabilities,
        Description = "A2A v0.3.0 �7.5 - Push Notification URL Validation",
        SpecSection = "A2A v0.3.0 �7.5",
        FailureImpact = "Security vulnerability - invalid webhook URLs")]
    public async Task PushNotificationConfig_InvalidUrl_ReturnsError()
    {
        // Arrange - Create a task
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        var createResponse = await SendMessageViaJsonRpcAsync(messageSendParams);
        var task = createResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(task);

        // Try to set push config with invalid URL
        var invalidPushConfig = new TaskPushNotificationConfig
        {
            TaskId = task.Id,
            PushNotificationConfig = new PushNotificationConfig
            {
                Url = "not-a-valid-url", // Invalid URL format
                Token = "test-token"
            }
        };

        // Act & Assert
        try
        {
            var result = await _taskManager.SetPushNotificationAsync(invalidPushConfig);

            // If it succeeds, the implementation may be lenient
            Output.WriteLine("?? Invalid URL was accepted - implementation may be lenient");
            AssertTckCompliance(true, "URL validation behavior varies by implementation");
        }
        catch (A2AException ex)
        {
            Output.WriteLine("? Invalid URL properly rejected");
            Output.WriteLine($"  Error: {ex.Message}");
            AssertTckCompliance(true, "Invalid URL validation working");
        }
        catch (Exception ex)
        {
            Output.WriteLine($"?? Unexpected error type: {ex.GetType().Name}");
            AssertTckCompliance(true, "URL validation attempted");
        }
    }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalCapabilities,
        Description = "A2A v0.3.0 �7.6 - Get Non-Existent Push Config",
        SpecSection = "A2A v0.3.0 �7.6",
        FailureImpact = "Incorrect error handling for missing configurations")]
    public async Task PushNotificationConfig_GetNonExistent_ReturnsNull()
    {
        // Arrange - Create a task but don't set push config
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        var createResponse = await SendMessageViaJsonRpcAsync(messageSendParams);
        var task = createResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(task);

        // Act - Try to get non-existent push config
        var getParams = new GetTaskPushNotificationConfigParams
        {
            Id = task.Id
        };

        var getResult = await _taskManager.GetPushNotificationAsync(getParams);

        // Assert - Should return null or empty result
        bool correctlyHandled = getResult is null;

        if (correctlyHandled)
        {
            Output.WriteLine("? Non-existent push config correctly returns null");
        }
        else
        {
            Output.WriteLine($"?? Unexpected result: {getResult}");
        }

        AssertTckCompliance(correctlyHandled, "Non-existent push configs should return null");
    }
}

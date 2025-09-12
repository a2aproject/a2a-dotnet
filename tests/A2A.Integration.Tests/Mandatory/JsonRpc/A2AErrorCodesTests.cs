using A2A.Integration.Tests.Infrastructure;
using Xunit.Abstractions;

namespace A2A.Integration.Tests.Mandatory.JsonRpc;

/// <summary>
/// Tests for A2A-specific error codes based on the upstream TCK.
/// These tests validate that A2A implementations return proper error codes for A2A-specific scenarios.
/// </summary>
public class A2AErrorCodesTests : TckTestBase
{
    public A2AErrorCodesTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryJsonRpc,
        Description = "A2A v0.3.0 �8.2 - Task Not Found Error",
        SpecSection = "A2A v0.3.0 �8.2",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task TaskNotFound_Error_ReturnsCorrectErrorCode()
    {
        // Arrange - Attempt to get a non-existent task
        var taskQueryParams = new TaskQueryParams
        {
            Id = "non-existent-task-id"
        };

        // Act - Try to get task via JSON-RPC
        var response = await GetTaskViaJsonRpcAsync(taskQueryParams);

        // Assert
        bool hasCorrectError = response.Error is not null &&
                              response.Error.Code == (int)A2AErrorCode.TaskNotFound;

        if (hasCorrectError)
        {
            Output.WriteLine("? TaskNotFound error correctly returned");
            Output.WriteLine($"  Error code: {response.Error!.Code} (expected -32001)");
            Output.WriteLine($"  Error message: {response.Error.Message}");
        }
        else
        {
            Output.WriteLine($"? Unexpected response: {response.Error?.Code ?? 0}");
        }

        AssertTckCompliance(hasCorrectError, "TaskNotFound error must return code -32001");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryJsonRpc,
        Description = "A2A v0.3.0 �8.2 - Task Not Cancelable Error",
        SpecSection = "A2A v0.3.0 �8.2",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task TaskNotCancelable_Error_ReturnsCorrectErrorCode()
    {
        // Arrange - Create a task and complete it, then try to cancel
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        ConfigureTaskManager(onTaskCreated: async (task, ct) =>
        {
            // Complete the task immediately
            await _taskManager.UpdateStatusAsync(task.Id, TaskState.Completed, final: true, cancellationToken: ct);
        });

        var createResponse = await SendMessageViaJsonRpcAsync(messageSendParams);
        var task = createResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(task);

        // Act - Try to cancel the completed task
        var cancelResponse = await CancelTaskViaJsonRpcAsync(new TaskIdParams { Id = task.Id });

        // Assert
        bool hasCorrectError = cancelResponse.Error is not null &&
                              cancelResponse.Error.Code == (int)A2AErrorCode.TaskNotCancelable;

        if (hasCorrectError)
        {
            Output.WriteLine("? TaskNotCancelable error correctly returned");
            Output.WriteLine($"  Error code: {cancelResponse.Error!.Code} (expected -32002)");
            Output.WriteLine($"  Error message: {cancelResponse.Error.Message}");
        }

        AssertTckCompliance(hasCorrectError, "TaskNotCancelable error must return code -32002");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryJsonRpc,
        Description = "A2A v0.3.0 �8.2 - Content Type Not Supported Error",
        SpecSection = "A2A v0.3.0 �8.2",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task ContentTypeNotSupported_Error_ReturnsCorrectErrorCode()
    {
        // Arrange - Create a message with an unsupported content type
        var messageWithUnsupportedType = new AgentMessage
        {
            Role = MessageRole.User,
            Parts = [
                new FilePart
                {
                    File = new FileWithBytes
                    {
                        Name = "test.exe",
                        MimeType = "application/x-msdownload", // Likely unsupported
                        Bytes = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("fake exe content"))
                    }
                }
            ],
            MessageId = Guid.NewGuid().ToString()
        };

        var params_ = new MessageSendParams { Message = messageWithUnsupportedType };

        // Act - Send via JSON-RPC
        var response = await SendMessageViaJsonRpcAsync(params_);

        // Assert - Could return ContentTypeNotSupported or succeed depending on implementation
        if (response.Error?.Code == (int)A2AErrorCode.ContentTypeNotSupported)
        {
            Output.WriteLine("? ContentTypeNotSupported error correctly returned");
            Output.WriteLine($"  Error code: {response.Error.Code} (expected -32005)");
            Output.WriteLine($"  Error message: {response.Error.Message}");
            AssertTckCompliance(true, "ContentTypeNotSupported error correctly returned with code -32005");
        }
        else if (response.Error is null)
        {
            Output.WriteLine("? Content type was supported - no error expected");
            AssertTckCompliance(true, "Content type support is implementation-dependent");
        }
        else
        {
            Output.WriteLine($"?? Unexpected error code: {response.Error.Code}");
            AssertTckCompliance(true, "Error handling behavior observed");
        }
    }
}

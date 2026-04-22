namespace A2A.UnitTests;

public class A2AErrorCodeMappingTests
{
    [Theory]
    [InlineData(A2AErrorCode.TaskNotFound, 404)]
    [InlineData(A2AErrorCode.TaskNotCancelable, 400)]
    [InlineData(A2AErrorCode.PushNotificationNotSupported, 400)]
    [InlineData(A2AErrorCode.UnsupportedOperation, 400)]
    [InlineData(A2AErrorCode.ContentTypeNotSupported, 400)]
    [InlineData(A2AErrorCode.InvalidAgentResponse, 500)]
    [InlineData(A2AErrorCode.ExtendedAgentCardNotConfigured, 400)]
    [InlineData(A2AErrorCode.ExtensionSupportRequired, 400)]
    [InlineData(A2AErrorCode.VersionNotSupported, 400)]
    [InlineData(A2AErrorCode.InvalidRequest, 400)]
    [InlineData(A2AErrorCode.MethodNotFound, 404)]
    [InlineData(A2AErrorCode.InvalidParams, 400)]
    [InlineData(A2AErrorCode.InternalError, 500)]
    [InlineData(A2AErrorCode.ParseError, 400)]
    public void GetHttpStatusCode_ReturnsCorrectStatus(A2AErrorCode code, int expected)
    {
        Assert.Equal(expected, A2AErrorCodeMapping.GetHttpStatusCode(code));
    }

    [Theory]
    [InlineData(A2AErrorCode.TaskNotFound, "NOT_FOUND")]
    [InlineData(A2AErrorCode.TaskNotCancelable, "FAILED_PRECONDITION")]
    [InlineData(A2AErrorCode.PushNotificationNotSupported, "FAILED_PRECONDITION")]
    [InlineData(A2AErrorCode.UnsupportedOperation, "FAILED_PRECONDITION")]
    [InlineData(A2AErrorCode.ContentTypeNotSupported, "INVALID_ARGUMENT")]
    [InlineData(A2AErrorCode.InvalidAgentResponse, "INTERNAL")]
    [InlineData(A2AErrorCode.ExtendedAgentCardNotConfigured, "FAILED_PRECONDITION")]
    [InlineData(A2AErrorCode.ExtensionSupportRequired, "FAILED_PRECONDITION")]
    [InlineData(A2AErrorCode.VersionNotSupported, "FAILED_PRECONDITION")]
    [InlineData(A2AErrorCode.InvalidRequest, "INVALID_ARGUMENT")]
    [InlineData(A2AErrorCode.MethodNotFound, "NOT_FOUND")]
    [InlineData(A2AErrorCode.InvalidParams, "INVALID_ARGUMENT")]
    [InlineData(A2AErrorCode.InternalError, "INTERNAL")]
    [InlineData(A2AErrorCode.ParseError, "INVALID_ARGUMENT")]
    public void GetGrpcStatus_ReturnsCorrectStatus(A2AErrorCode code, string expected)
    {
        Assert.Equal(expected, A2AErrorCodeMapping.GetGrpcStatus(code));
    }

    [Theory]
    [InlineData(A2AErrorCode.TaskNotFound, "TASK_NOT_FOUND")]
    [InlineData(A2AErrorCode.TaskNotCancelable, "TASK_NOT_CANCELABLE")]
    [InlineData(A2AErrorCode.PushNotificationNotSupported, "PUSH_NOTIFICATION_NOT_SUPPORTED")]
    [InlineData(A2AErrorCode.UnsupportedOperation, "UNSUPPORTED_OPERATION")]
    [InlineData(A2AErrorCode.ContentTypeNotSupported, "CONTENT_TYPE_NOT_SUPPORTED")]
    [InlineData(A2AErrorCode.InvalidAgentResponse, "INVALID_AGENT_RESPONSE")]
    [InlineData(A2AErrorCode.ExtendedAgentCardNotConfigured, "EXTENDED_AGENT_CARD_NOT_CONFIGURED")]
    [InlineData(A2AErrorCode.ExtensionSupportRequired, "EXTENSION_SUPPORT_REQUIRED")]
    [InlineData(A2AErrorCode.VersionNotSupported, "VERSION_NOT_SUPPORTED")]
    public void GetReasonString_ReturnsCorrectReason_ForA2ASpecificCodes(A2AErrorCode code, string expected)
    {
        Assert.Equal(expected, A2AErrorCodeMapping.GetReasonString(code));
    }

    [Theory]
    [InlineData(A2AErrorCode.InvalidRequest)]
    [InlineData(A2AErrorCode.MethodNotFound)]
    [InlineData(A2AErrorCode.InvalidParams)]
    [InlineData(A2AErrorCode.InternalError)]
    [InlineData(A2AErrorCode.ParseError)]
    public void GetReasonString_ReturnsNull_ForStandardJsonRpcCodes(A2AErrorCode code)
    {
        Assert.Null(A2AErrorCodeMapping.GetReasonString(code));
    }

    [Theory]
    [InlineData(A2AErrorCode.TaskNotFound, true)]
    [InlineData(A2AErrorCode.TaskNotCancelable, true)]
    [InlineData(A2AErrorCode.PushNotificationNotSupported, true)]
    [InlineData(A2AErrorCode.UnsupportedOperation, true)]
    [InlineData(A2AErrorCode.ContentTypeNotSupported, true)]
    [InlineData(A2AErrorCode.InvalidAgentResponse, true)]
    [InlineData(A2AErrorCode.ExtendedAgentCardNotConfigured, true)]
    [InlineData(A2AErrorCode.ExtensionSupportRequired, true)]
    [InlineData(A2AErrorCode.VersionNotSupported, true)]
    [InlineData(A2AErrorCode.InvalidRequest, false)]
    [InlineData(A2AErrorCode.MethodNotFound, false)]
    [InlineData(A2AErrorCode.InvalidParams, false)]
    [InlineData(A2AErrorCode.InternalError, false)]
    [InlineData(A2AErrorCode.ParseError, false)]
    public void IsA2ASpecificError_ReturnsCorrectResult(A2AErrorCode code, bool expected)
    {
        Assert.Equal(expected, A2AErrorCodeMapping.IsA2ASpecificError(code));
    }
}

namespace A2A;

/// <summary>
/// Standard JSON-RPC error codes used in A2A protocol.
/// </summary>
public enum A2AErrorCode
{
    /// <summary>
    /// Task not found - The specified task does not exist.
    /// </summary>
    TaskNotFound = -32001,

    /// <summary>
    /// Task not cancelable - The task cannot be canceled.
    /// </summary>
    TaskNotCancelable = -32002,

    /// <summary>
    /// Push notification not supported - Push notifications are not supported.
    /// </summary>
    PushNotificationNotSupported = -32003,

    /// <summary>
    /// Unsupported operation - The requested operation is not supported.
    /// </summary>
    UnsupportedOperation = -32004,

    /// <summary>
    /// Content type not supported - The content type is not supported.
    /// </summary>
    ContentTypeNotSupported = -32005,

    /// <summary>
    /// Invalid agent response - The agent returned an invalid response.
    /// </summary>
    InvalidAgentResponse = -32006,

    /// <summary>
    /// Extended agent card not configured - The extended agent card feature is not configured.
    /// </summary>
    ExtendedAgentCardNotConfigured = -32007,

    /// <summary>
    /// Extension support required - The requested extension is required but not supported.
    /// </summary>
    ExtensionSupportRequired = -32008,

    /// <summary>
    /// Version not supported - The requested protocol version is not supported.
    /// </summary>
    VersionNotSupported = -32009,

    /// <summary>
    /// Invalid request - The JSON is not a valid Request object.
    /// </summary>
    InvalidRequest = -32600,

    /// <summary>
    /// Method not found - The method does not exist or is not available.
    /// </summary>
    MethodNotFound = -32601,

    /// <summary>
    /// Invalid params - Invalid method parameters.
    /// </summary>
    InvalidParams = -32602,

    /// <summary>
    /// Internal error - Internal JSON-RPC error.
    /// </summary>
    InternalError = -32603,

    /// <summary>
    /// Parse error - Invalid JSON received.
    /// </summary>
    ParseError = -32700,
}
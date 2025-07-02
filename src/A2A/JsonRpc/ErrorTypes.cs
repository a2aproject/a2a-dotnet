namespace A2A;

/// <summary>
/// Base exception for A2A client errors
/// </summary>
public class A2AClientError : Exception
{
    /// <summary>
    /// Creates a new A2A client error
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">Optional inner exception</param>
    public A2AClientError(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception for HTTP errors
/// </summary>
public class A2AClientHTTPError : A2AClientError
{
    /// <summary>
    /// The HTTP status code
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// The error message
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// Creates a new HTTP error
    /// </summary>
    /// <param name="statusCode">The HTTP status code</param>
    /// <param name="message">The error message</param>
    public A2AClientHTTPError(int statusCode, string message)
        : base($"HTTP Error {statusCode}: {message}")
    {
        StatusCode = statusCode;
        ErrorMessage = message;
    }
}

/// <summary>
/// Exception for JSON parsing errors
/// </summary>
public class A2AClientJsonError : A2AClientError
{
    /// <summary>
    /// The error message
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// Creates a new JSON error
    /// </summary>
    /// <param name="message">The error message</param>
    public A2AClientJsonError(string message)
        : base($"JSON Error: {message}")
    {
        ErrorMessage = message;
    }
}

/// <summary>
/// Exception for missing API key
/// </summary>
public class MissingAPIKeyError : Exception
{
    /// <summary>
    /// Creates a new missing API key error
    /// </summary>
    public MissingAPIKeyError()
        : base("API key is required but was not provided")
    {
    }
}

/// <summary>
/// Error for internal server errors
/// </summary>
public class InternalError : JsonRpcError
{
    /// <summary>
    /// Creates a new internal error
    /// </summary>
    public InternalError()
    {
        Code = -32603;
        Message = "Internal error";
    }
}

/// <summary>
/// Error for task not found
/// </summary>
public class TaskNotFoundError : JsonRpcError
{
    /// <summary>
    /// Creates a new task not found error
    /// </summary>
    public TaskNotFoundError()
    {
        Code = -32001;
        Message = "Task not found";
    }
}

/// <summary>
/// Error for task not cancelable
/// </summary>
public class TaskNotCancelableError : JsonRpcError
{
    /// <summary>
    /// Creates a new task not cancelable error
    /// </summary>
    public TaskNotCancelableError()
    {
        Code = -32002;
        Message = "Task cannot be canceled";
    }
}

/// <summary>
/// Error for push notification not supported
/// </summary>
public class PushNotificationNotSupportedError : JsonRpcError
{
    /// <summary>
    /// Creates a new push notification not supported error
    /// </summary>
    public PushNotificationNotSupportedError()
    {
        Code = -32003;
        Message = "Push Notification is not supported";
    }
}

/// <summary>
/// Error for unsupported operations
/// </summary>
public class UnsupportedOperationError : JsonRpcError
{
    /// <summary>
    /// Creates a new unsupported operation error
    /// </summary>
    public UnsupportedOperationError()
    {
        Code = -32004;
        Message = "This operation is not supported";
    }
}

/// <summary>
/// Error for incompatible content types
/// </summary>
public class ContentTypeNotSupportedError : JsonRpcError
{
    /// <summary>
    /// Creates a new content type not supported error
    /// </summary>
    public ContentTypeNotSupportedError()
    {
        Code = -32005;
        Message = "Incompatible content types";
    }
}



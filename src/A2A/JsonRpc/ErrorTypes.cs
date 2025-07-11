using System.Net;

namespace A2A;

/// <summary>
/// Base exception for A2A client errors.
/// </summary>
public class A2AClientException : Exception
{
    /// <summary>
    /// Creates a new A2A client error.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">Optional inner exception.</param>
    public A2AClientException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception for HTTP errors.
/// </summary>
public sealed class A2AClientHTTPException : A2AClientException
{
    /// <summary>
    /// The HTTP status code.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The error message.
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// Creates a new HTTP error.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="message">The error message.</param>
    public A2AClientHTTPException(HttpStatusCode statusCode, string message)
        : base($"HTTP Error {(int)statusCode}: {message}")
    {
        StatusCode = statusCode;
        ErrorMessage = message;
    }
}

/// <summary>
/// Exception for JSON parsing errors.
/// </summary>
public sealed class A2AClientJsonException : A2AClientException
{
    /// <summary>
    /// The error message.
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// Creates a new JSON error.
    /// </summary>
    /// <param name="message">The error message.</param>
    public A2AClientJsonException(string message)
        : base($"JSON Error: {message}")
    {
        ErrorMessage = message;
    }
}

/// <summary>
/// Exception for missing API key.
/// </summary>
public sealed class MissingAPIKeyException : Exception
{
    /// <summary>
    /// Creates a new missing API key error.
    /// </summary>
    public MissingAPIKeyException()
        : base("API key is required but was not provided")
    {
    }
}
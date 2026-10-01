namespace A2A;

/// <summary>Represents a declared typed A2A operation error.</summary>
/// <typeparam name="TDetails">The error details payload type.</typeparam>
public sealed class A2AOperationError<TDetails>
{
    internal A2AOperationError(string errorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorId);
        ErrorId = errorId;
    }

    /// <summary>Gets the stable error identifier.</summary>
    public string ErrorId { get; }
}

/// <summary>Represents an exception raised for a declared A2A operation error.</summary>
public abstract class A2AOperationException : Exception
{
    /// <summary>Initializes a new operation exception.</summary>
    /// <param name="errorId">The declared error identifier.</param>
    /// <param name="message">The exception message.</param>
    protected A2AOperationException(string errorId, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorId);
        ErrorId = errorId;
    }

    /// <summary>Gets the declared error identifier.</summary>
    public string ErrorId { get; }
}

/// <summary>Represents an exception raised for a declared typed A2A operation error.</summary>
/// <typeparam name="TDetails">The error details payload type.</typeparam>
public sealed class A2AOperationException<TDetails> : A2AOperationException
{
    /// <summary>Initializes a new typed operation exception.</summary>
    /// <param name="error">The declared error handle.</param>
    /// <param name="message">The exception message.</param>
    /// <param name="details">The typed error details.</param>
    public A2AOperationException(
        A2AOperationError<TDetails> error,
        string message,
        TDetails details)
        : base(error?.ErrorId ?? throw new ArgumentNullException(nameof(error)), message)
    {
        Error = error;
        Details = details;
    }

    /// <summary>Gets the declared error handle.</summary>
    public A2AOperationError<TDetails> Error { get; }

    /// <summary>Gets the typed error details.</summary>
    public TDetails Details { get; }
}

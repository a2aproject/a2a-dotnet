namespace A2A;

/// <summary>Identifies an A2A operation independently of its transport binding.</summary>
public readonly record struct A2AOperationId
{
    /// <summary>Initializes a new operation identifier.</summary>
    /// <param name="value">The stable operation identifier.</param>
    public A2AOperationId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>Gets the stable operation identifier.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies whether an A2A operation is unary or streaming.</summary>
public enum A2AOperationKind
{
    /// <summary>The operation returns one result.</summary>
    Unary,

    /// <summary>The operation returns a stream of events.</summary>
    Streaming,
}

/// <summary>Performs semantic validation for a typed operation request.</summary>
/// <typeparam name="TRequest">The operation request type.</typeparam>
/// <param name="request">The typed operation request.</param>
public delegate void A2AOperationValidator<in TRequest>(TRequest request);

/// <summary>Defines a typed unary A2A operation.</summary>
/// <typeparam name="TRequest">The operation request type.</typeparam>
/// <typeparam name="TResult">The operation result type.</typeparam>
public sealed class A2AOperation<TRequest, TResult> : IA2AOperationHandle
{
    /// <summary>Initializes a new typed operation.</summary>
    /// <param name="id">The transport-neutral operation identifier.</param>
    internal A2AOperation(A2AOperationId id)
    {
        Id = id;
    }

    /// <summary>Gets the transport-neutral operation identifier.</summary>
    public A2AOperationId Id { get; }
}

/// <summary>Defines a typed streaming A2A operation.</summary>
/// <typeparam name="TRequest">The operation request type.</typeparam>
/// <typeparam name="TEvent">The streamed event type.</typeparam>
public sealed class A2AStreamingOperation<TRequest, TEvent> : IA2AOperationHandle
{
    /// <summary>Initializes a new typed streaming operation.</summary>
    /// <param name="id">The transport-neutral operation identifier.</param>
    internal A2AStreamingOperation(A2AOperationId id)
    {
        Id = id;
    }

    /// <summary>Gets the transport-neutral operation identifier.</summary>
    public A2AOperationId Id { get; }
}

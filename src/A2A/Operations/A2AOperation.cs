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

/// <summary>Defines a typed unary A2A operation.</summary>
/// <typeparam name="TRequest">The operation request type.</typeparam>
/// <typeparam name="TResult">The operation result type.</typeparam>
public sealed class A2AOperation<TRequest, TResult>
{
    /// <summary>Initializes a new typed operation.</summary>
    /// <param name="id">The transport-neutral operation identifier.</param>
    public A2AOperation(A2AOperationId id)
    {
        Id = id;
    }

    /// <summary>Gets the transport-neutral operation identifier.</summary>
    public A2AOperationId Id { get; }
}

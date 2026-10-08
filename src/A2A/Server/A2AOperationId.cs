namespace A2A;

/// <summary>
/// Identifies a custom A2A operation independently of any transport binding.
/// </summary>
/// <param name="Value">The stable operation identifier.</param>
public readonly record struct A2AOperationId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value;
}

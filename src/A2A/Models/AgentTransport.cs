using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents the transport protocol for an AgentInterface.
/// </summary>
public readonly struct AgentTransport : IEquatable<AgentTransport>
{
    /// <summary>
    /// JSON-RPC transport.
    /// </summary>
    public static AgentTransport JsonRpc { get; } = new("JSONRPC");

    /// <summary>
    /// Gets the label associated with this <see cref="AgentTransport"/>.
    /// </summary>
    [JsonPropertyName("transport")]
    public string Label { get; }

    /// <summary>
    /// Creates a new <see cref="AgentTransport"/> instance with the provided label.
    /// </summary>
    /// <param name="label">The label to associate with this <see cref="AgentTransport"/>.</param>
    [JsonConstructor]
    public AgentTransport(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Transport label cannot be null or whitespace.", nameof(label));
        this.Label = label;
    }

    /// <summary>
    /// Determines whether two <see cref="AgentTransport"/> instances are equal.
    /// </summary>
    /// <param name="left">The first <see cref="AgentTransport"/> to compare.</param>
    /// <param name="right">The second <see cref="AgentTransport"/> to compare.</param>
    /// <returns><c>true</c> if the instances are equal; otherwise, <c>false</c>.</returns>
    public static bool operator ==(AgentTransport left, AgentTransport right)
        => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="AgentTransport"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first <see cref="AgentTransport"/> to compare.</param>
    /// <param name="right">The second <see cref="AgentTransport"/> to compare.</param>
    /// <returns><c>true</c> if the instances are not equal; otherwise, <c>false</c>.</returns>
    public static bool operator !=(AgentTransport left, AgentTransport right)
        => !(left == right);

    /// <summary>
    /// Determines whether the specified object is equal to the current <see cref="AgentTransport"/>.
    /// </summary>
    /// <param name="obj">The object to compare with the current <see cref="AgentTransport"/>.</param>
    /// <returns><c>true</c> if the specified object is equal to the current <see cref="AgentTransport"/>; otherwise, <c>false</c>.</returns>
    public override bool Equals(object? obj)
        => obj is AgentTransport other && this == other;

    /// <summary>
    /// Determines whether the specified <see cref="AgentTransport"/> is equal to the current <see cref="AgentTransport"/>.
    /// </summary>
    /// <param name="other">The <see cref="AgentTransport"/> to compare with the current <see cref="AgentTransport"/>.</param>
    /// <returns><c>true</c> if the specified <see cref="AgentTransport"/> is equal to the current <see cref="AgentTransport"/>; otherwise, <c>false</c>.</returns>
    public bool Equals(AgentTransport other)
        => string.Equals(this.Label, other.Label, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the hash code for this <see cref="AgentTransport"/>.
    /// </summary>
    /// <returns>A hash code for the current <see cref="AgentTransport"/>.</returns>
    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(this.Label);

    /// <summary>
    /// Returns the string representation of this <see cref="AgentTransport"/>.
    /// </summary>
    /// <returns>The label of this <see cref="AgentTransport"/>.</returns>
    public override string ToString() => this.Label;
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.V0_3;

/// <summary>
/// Represents the transport protocol for an AgentInterface.
/// </summary>
[JsonConverter(typeof(AgentTransportConverter))]
public readonly struct AgentTransport : IEquatable<AgentTransport>
{
    /// <summary>
    /// JSON-RPC transport.
    /// </summary>
    public static AgentTransport JsonRpc { get; } = new("JSONRPC");

    /// <summary>
    /// Gets the label associated with this <see cref="AgentTransport"/>.
    /// </summary>
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

    /// <summary>
    /// Custom JSON converter for <see cref="AgentTransport"/> that serializes it as a simple string value.
    /// </summary>
    internal sealed class AgentTransportConverter : JsonConverter<AgentTransport>
    {
        /// <summary>
        /// Reads and converts the JSON to <see cref="AgentTransport"/>.
        /// </summary>
        /// <param name="reader">The reader to read from.</param>
        /// <param name="typeToConvert">The type to convert.</param>
        /// <param name="options">Serializer options.</param>
        /// <returns>The converted <see cref="AgentTransport"/>.</returns>
        public override AgentTransport Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Expected a string for AgentTransport.");
            }

            var label = reader.GetString();
            if (string.IsNullOrWhiteSpace(label))
            {
                throw new JsonException("AgentTransport string value cannot be null or whitespace.");
            }

            return new AgentTransport(label!);
        }

        /// <summary>
        /// Writes the <see cref="AgentTransport"/> as a JSON string.
        /// </summary>
        /// <param name="writer">The writer to write to.</param>
        /// <param name="value">The value to convert.</param>
        /// <param name="options">Serializer options.</param>
        public override void Write(Utf8JsonWriter writer, AgentTransport value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Label);
        }
    }
}

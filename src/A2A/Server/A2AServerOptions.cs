namespace A2A;

/// <summary>
/// Configuration options for <see cref="A2AServer"/>.
/// </summary>
public sealed class A2AServerOptions
{
    /// <summary>
    /// Whether the agent supports streaming message responses. Default: true.
    /// </summary>
    /// <remarks>
    /// <c>AddA2AAgent</c> derives this value from the registered agent card,
    /// overriding any value set by its options configuration callback.
    /// </remarks>
    public bool SupportsStreaming { get; set; } = true;

    /// <summary>
    /// Media types accepted by the agent for incoming message parts.
    /// </summary>
    /// <remarks>
    /// <c>AddA2AAgent</c> derives this value from the registered agent card,
    /// overriding any value set by its options configuration callback.
    /// An empty collection disables media-type validation.
    /// </remarks>
    public IReadOnlyList<string> SupportedInputModes { get; set; } = [];

    /// <summary>
    /// Whether the agent advertises support for an extended agent card. Default: false.
    /// </summary>
    /// <remarks>
    /// <c>AddA2AAgent</c> derives this value from the registered agent card,
    /// overriding any value set by its options configuration callback.
    /// </remarks>
    public bool SupportsExtendedAgentCard { get; set; }

    /// <summary>
    /// Whether to automatically append the incoming user message to task history
    /// on continuation requests. Default: true.
    /// </summary>
    public bool AutoAppendHistory { get; set; } = true;
}

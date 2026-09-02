namespace A2A;

/// <summary>
/// Configuration options for <see cref="A2AServer"/>.
/// </summary>
public sealed class A2AServerOptions
{
    /// <summary>
    /// Whether the agent advertises support for an extended agent card. Default: false.
    /// </summary>
    /// <remarks>
    /// <c>AddA2AAgent</c> derives this value from the registered agent card.
    /// </remarks>
    public bool SupportsExtendedAgentCard { get; set; }

    /// <summary>
    /// Whether to automatically append the incoming user message to task history
    /// on continuation requests. Default: true.
    /// </summary>
    public bool AutoAppendHistory { get; set; } = true;
}

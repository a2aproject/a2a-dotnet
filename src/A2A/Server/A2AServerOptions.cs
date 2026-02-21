namespace A2A;

/// <summary>
/// Configuration options for <see cref="A2AServer"/>.
/// </summary>
public sealed class A2AServerOptions
{
    /// <summary>
    /// Whether to automatically append the incoming user message to task history
    /// on continuation requests. Default: true.
    /// </summary>
    public bool AutoAppendHistory { get; set; } = true;

    /// <summary>
    /// Whether to automatically persist events to the store as they flow
    /// through during streaming. Default: true.
    /// </summary>
    public bool AutoPersistEvents { get; set; } = true;
}

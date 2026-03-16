namespace A2A.V0_3;

/// <summary>
/// Defines the set of A2A event kinds used as the 'kind' discriminator in serialized payloads.
/// </summary>
/// <remarks>
/// Values are serialized as lowercase kebab-case strings.
/// </remarks>
internal static class A2AEventKind
{
    /// <summary>
    /// A conversational message from an agent.
    /// </summary>
    /// <seealso cref="AgentMessage"/>
    public const string Message = "message";

    /// <summary>
    /// A task issued to or produced by an agent.
    /// </summary>
    /// <seealso cref="AgentTask"/>
    public const string Task = "task";

    /// <summary>
    /// An update describing the current state of a task execution.
    /// </summary>
    /// <seealso cref="TaskStatusUpdateEvent"/>
    public const string StatusUpdate = "status-update";

    /// <summary>
    /// A notification that artifacts associated with a task have changed.
    /// </summary>
    /// <seealso cref="TaskArtifactUpdateEvent"/>
    public const string ArtifactUpdate = "artifact-update";
}
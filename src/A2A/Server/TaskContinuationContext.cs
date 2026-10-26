namespace A2A;

/// <summary>Task-oriented context for server-side event production without an inbound message.</summary>
/// <param name="Task">An independent snapshot of the existing task.</param>
/// <param name="EventQueue">The server-owned projection and publication queue.</param>
/// <param name="RequestContext">The resolved context, with no incoming message or client configuration.</param>
public sealed record TaskContinuationContext(
    AgentTask Task, AgentEventQueue EventQueue, RequestContext RequestContext)
{
    /// <summary>Gets the authoritative task identifier.</summary>
    public string TaskId => RequestContext.TaskId;

    /// <summary>Gets the authoritative context identifier.</summary>
    public string ContextId => RequestContext.ContextId;
}

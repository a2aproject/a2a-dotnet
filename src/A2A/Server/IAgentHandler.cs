namespace A2A;

/// <summary>
/// Defines the agent's execution logic. Implement this for the easy path
/// where <see cref="A2AServer"/> handles task lifecycle, persistence,
/// and observability.
/// </summary>
public interface IAgentHandler
{
    /// <summary>
    /// Execute agent logic for an incoming message.
    /// Use <see cref="TaskUpdater"/> to emit task lifecycle events, or write
    /// directly to <paramref name="eventQueue"/> for message-only responses.
    /// </summary>
    /// <param name="context">Pre-resolved context with IDs, existing task, and message.</param>
    /// <param name="eventQueue">Channel to write response events to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExecuteAsync(AgentContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken);

    /// <summary>
    /// Handle task cancellation. Default implementation transitions the task
    /// to <see cref="TaskState.Canceled"/> state. Override for custom cleanup
    /// logic (abort LLM calls, release resources).
    /// </summary>
    /// <param name="context">Pre-resolved context with IDs, existing task, and message.</param>
    /// <param name="eventQueue">Channel to write response events to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    async Task CancelAsync(AgentContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
        await updater.CancelAsync(cancellationToken);
    }
}

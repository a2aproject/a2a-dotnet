using A2A;

namespace MultiAgentHost;

/// <summary>
/// Simple echo agent that prefixes responses with the agent name.
/// Uses task-based responses so tasks are persisted and isolation can be validated.
/// </summary>
public sealed class NamedEchoAgent(string agentName) : IAgentHandler
{
    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
        await updater.SubmitAsync(cancellationToken);
        await updater.AddArtifactAsync(
            [Part.FromText($"[{agentName}] Echo: {context.UserText}")], cancellationToken: cancellationToken);
        await updater.CompleteAsync(cancellationToken: cancellationToken);
    }
}

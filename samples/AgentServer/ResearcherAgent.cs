using A2A;

namespace AgentServer;

public sealed class ResearcherAgent : IAgentHandler
{
    public async Task ExecuteAsync(AgentContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);

        if (!context.IsContinuation)
        {
            // New task: planning phase — ask for confirmation
            await updater.SubmitAsync(cancellationToken);
            await updater.AddArtifactAsync(
                [Part.FromText($"{context.UserText} received.")],
                cancellationToken: cancellationToken);

            await Task.Delay(500, cancellationToken);

            await updater.RequireInputAsync(new Message
            {
                Role = Role.Agent,
                MessageId = Guid.NewGuid().ToString("N"),
                ContextId = context.ContextId,
                Parts = [Part.FromText("When ready say go ahead")],
            }, cancellationToken);
            return;
        }

        // Continuation
        if (context.UserText == "go ahead")
        {
            // Research phase
            await updater.StartWorkAsync(cancellationToken: cancellationToken);
            await updater.AddArtifactAsync(
                [Part.FromText($"{context.UserText} received.")],
                cancellationToken: cancellationToken);
            await updater.CompleteAsync(
                new Message
                {
                    Role = Role.Agent,
                    MessageId = Guid.NewGuid().ToString("N"),
                    Parts = [Part.FromText("Task completed successfully")],
                },
                cancellationToken);
        }
        else
        {
            // Re-plan — ask again
            await Task.Delay(500, cancellationToken);
            await updater.AddArtifactAsync(
                [Part.FromText($"{context.UserText} received.")],
                cancellationToken: cancellationToken);
            await updater.RequireInputAsync(new Message
            {
                Role = Role.Agent,
                MessageId = Guid.NewGuid().ToString("N"),
                ContextId = context.ContextId,
                Parts = [Part.FromText("When ready say go ahead")],
            }, cancellationToken);
        }
    }

    public static AgentCard GetAgentCard(string agentUrl) =>
        new()
        {
            Name = "Researcher Agent",
            Description = "Agent which conducts research.",
            Version = "1.0.0",
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    Url = agentUrl,
                    ProtocolBinding = "JSONRPC",
                    ProtocolVersion = "1.0",
                }
            ],
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["text/plain"],
            Capabilities = new AgentCapabilities
            {
                Streaming = true,
                PushNotifications = false,
            },
            Skills =
            [
                new AgentSkill
                {
                    Id = "research",
                    Name = "Research",
                    Description = "Conducts research on a given topic.",
                    Tags = ["research", "planning"],
                }
            ],
        };
}
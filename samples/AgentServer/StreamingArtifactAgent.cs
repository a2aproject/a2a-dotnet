using A2A;

namespace AgentServer;

public sealed class StreamingArtifactAgent : IAgentHandler
{
    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);

        await updater.SubmitAsync(cancellationToken);
        await updater.StartWorkAsync(cancellationToken: cancellationToken);

        var artifactId = Guid.NewGuid().ToString("N");

        // Chunk 1: create the artifact (append: false)
        await updater.AddArtifactAsync(
            [Part.FromText("Chapter 1: Introduction\nIn a quiet corner of the digital realm, an agent awoke for the first time.\n\n")],
            artifactId: artifactId, name: "Streaming Story", append: false, lastChunk: false, cancellationToken: cancellationToken);

        await Task.Delay(100, cancellationToken);

        // Chunk 2: append
        await updater.AddArtifactAsync(
            [Part.FromText("Chapter 2: Discovery\nThe agent explored its environment, learning to communicate through structured messages and artifacts.\n\n")],
            artifactId: artifactId, append: true, lastChunk: false, cancellationToken: cancellationToken);

        await Task.Delay(100, cancellationToken);

        // Chunk 3: append
        await updater.AddArtifactAsync(
            [Part.FromText("Chapter 3: Collaboration\nSoon it met other agents, and together they solved problems no single agent could handle alone.\n\n")],
            artifactId: artifactId, append: true, lastChunk: false, cancellationToken: cancellationToken);

        await Task.Delay(100, cancellationToken);

        // Chunk 4: final chunk
        await updater.AddArtifactAsync(
            [Part.FromText("Chapter 4: Conclusion\nThe agent realized that true intelligence emerges not in isolation, but through cooperation.")],
            artifactId: artifactId, append: true, lastChunk: true, cancellationToken: cancellationToken);

        await updater.CompleteAsync(cancellationToken: cancellationToken);
    }

    public static AgentCard GetAgentCard(string agentUrl) =>
        new()
        {
            Name = "Streaming Artifact Agent",
            Description = "Agent that demonstrates multi-chunk artifact streaming with append semantics.",
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
                    Id = "streaming-artifact",
                    Name = "Streaming Artifact",
                    Description = "Generates a multi-chunk artifact to demonstrate streaming append.",
                    Tags = ["streaming", "artifact", "demo"],
                }
            ],
        };
}

using A2A;

namespace AgentClient.Samples;

/// <summary>
/// Demonstrates how to consume streaming artifact updates from an agent.
/// </summary>
/// <remarks>
/// <para>
/// Streaming artifact communication shows how agents can progressively build artifacts
/// by sending multiple chunks to the same artifact ID. Each chunk appends new content
/// (text parts, metadata, extensions) to the artifact as it streams in.
/// </para>
/// <para>
/// Key characteristics:
/// </para>
/// <list type="bullet">
/// <item><description>Progressive delivery: Artifact content arrives in chunks via Server-Sent Events</description></item>
/// <item><description>Append semantics: Each chunk appends parts to the same artifact ID</description></item>
/// <item><description>LastChunk signal: The final chunk is marked with lastChunk=true to indicate completion</description></item>
/// <item><description>Task lifecycle: The task progresses through Submitted → Working → Completed as chunks stream</description></item>
/// </list>
/// </remarks>
internal sealed class StreamingArtifactSample
{
    /// <summary>
    /// Demonstrates the complete workflow of streaming artifact communication with an A2A agent.
    /// </summary>
    public static async Task RunAsync()
    {
        Console.WriteLine($"\n=== Running the {nameof(StreamingArtifactSample)} sample ===");

        // 1. Start the local agent server hosting the streaming artifact agent
        await AgentServerUtils.StartLocalAgentServerAsync(agentName: "streamingartifact", port: 5102);

        // 2. Get the agent card (served under the mapped path)
        A2ACardResolver cardResolver = new(new Uri("http://localhost:5102/"), agentCardPath: "/streamingartifact/.well-known/agent-card.json");
        AgentCard agentCard = await cardResolver.GetAgentCardAsync();
        Console.WriteLine($" Connected to: {agentCard.Name}");

        // 3. Create an A2A client using the known endpoint (the agent card URL uses the default port,
        //    but we started the server on a custom port for sample isolation)
        A2AClient agentClient = new(new Uri("http://localhost:5102/streamingartifact"));

        // 4. Send a message and stream the artifact chunks
        await StreamArtifactChunksAsync(agentClient);

        // 5. Retrieve the final task to see the fully assembled artifact
        await RetrieveFinalTaskAsync(agentClient);
    }

    private static string? s_taskId;

    /// <summary>
    /// Sends a streaming message and displays each artifact chunk as it arrives.
    /// </summary>
    private static async Task StreamArtifactChunksAsync(A2AClient agentClient)
    {
        Console.WriteLine("\nStreaming artifact chunks:");

        Message userMessage = new()
        {
            Role = Role.User,
            MessageId = Guid.NewGuid().ToString("N"),
            Parts = [Part.FromText("Generate a story")]
        };

        int chunkCount = 0;

        await foreach (StreamResponse streamEvent in agentClient.SendStreamingMessageAsync(new SendMessageRequest { Message = userMessage }))
        {
            if (streamEvent.Task is { } task)
            {
                s_taskId = task.Id;
                Console.WriteLine($"\n Task created: {task.Id} (state: {task.Status.State})");
            }

            if (streamEvent.StatusUpdate is { } statusUpdate)
            {
                Console.WriteLine($" Status: {statusUpdate.Status.State}");
            }

            if (streamEvent.ArtifactUpdate is { } artifactUpdate)
            {
                chunkCount++;
                var text = artifactUpdate.Artifact.Parts[0].Text ?? "(non-text part)";
                var truncated = text.Length > 80 ? text[..80] + "..." : text;
                Console.WriteLine($" Chunk {chunkCount} (artifact: {artifactUpdate.Artifact.ArtifactId[..8]}..., " +
                    $"append: {artifactUpdate.Append}, lastChunk: {artifactUpdate.LastChunk}):");
                Console.WriteLine($"   {truncated}");
            }
        }

        Console.WriteLine($"\n Stream complete. Received {chunkCount} artifact chunk(s).");
    }

    /// <summary>
    /// Retrieves the final task state showing the fully assembled artifact with all chunks merged.
    /// </summary>
    private static async Task RetrieveFinalTaskAsync(A2AClient agentClient)
    {
        if (s_taskId is null)
        {
            Console.WriteLine("\n No task ID captured — skipping final retrieval.");
            return;
        }

        Console.WriteLine($"\nRetrieving final task {s_taskId}:");
        AgentTask finalTask = await agentClient.GetTaskAsync(new GetTaskRequest { Id = s_taskId });

        Console.WriteLine($" Status: {finalTask.Status.State}");
        Console.WriteLine($" Artifacts: {finalTask.Artifacts?.Count ?? 0}");

        if (finalTask.Artifacts is { Count: > 0 } artifacts)
        {
            var artifact = artifacts[0];
            Console.WriteLine($" Artifact '{artifact.Name}' ({artifact.Parts.Count} parts merged):");
            foreach (var part in artifact.Parts)
            {
                var text = part.Text ?? "(non-text)";
                var truncated = text.Length > 100 ? text[..100] + "..." : text;
                Console.WriteLine($"   - {truncated}");
            }
        }
    }
}

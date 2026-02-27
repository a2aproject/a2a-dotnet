using A2A;

namespace AgentServer;

/// <summary>
/// A sample agent that demonstrates streaming artifacts using TaskArtifactUpdateEvent.
/// It generates a story in chunks, streaming each paragraph as a separate artifact update.
/// </summary>
public class StreamingArtifactAgent
{
    private ITaskManager? _taskManager;

    public void Attach(ITaskManager taskManager)
    {
        _taskManager = taskManager;
        taskManager.OnTaskCreated = ProcessMessageAsync;
        taskManager.OnAgentCardQuery = GetAgentCardAsync;
    }

    private async Task ProcessMessageAsync(AgentTask task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lastMessage = task.History!.Last();
        var prompt = lastMessage.Parts.OfType<TextPart>().FirstOrDefault()?.Text ?? "a mysterious journey";

        await _taskManager!.UpdateStatusAsync(
            task.Id,
            status: TaskState.Working,
            cancellationToken: cancellationToken);

        // Stream a story as multiple artifact chunks
        var artifactId = $"story-{Guid.NewGuid():N}";
        var paragraphs = GenerateStory(prompt);

        for (int i = 0; i < paragraphs.Length; i++)
        {
            bool isFirst = i == 0;
            bool isLast = i == paragraphs.Length - 1;

            await _taskManager.ReturnArtifactStreamAsync(new TaskArtifactUpdateEvent
            {
                TaskId = task.Id,
                Artifact = new Artifact
                {
                    ArtifactId = artifactId,
                    Name = isFirst ? $"Story: {prompt}" : null,
                    Description = isFirst ? "A story generated in streaming chunks" : null,
                    Parts = [new TextPart { Text = paragraphs[i] }]
                },
                Append = !isFirst,
                LastChunk = isLast
            }, cancellationToken: cancellationToken);

            // Simulate generation delay
            await Task.Delay(500, cancellationToken);
        }

        await _taskManager.UpdateStatusAsync(
            task.Id,
            status: TaskState.Completed,
            final: true,
            cancellationToken: cancellationToken);
    }

    private static string[] GenerateStory(string prompt)
    {
        return
        [
            $"Once upon a time, in a land inspired by \"{prompt}\", there lived a curious adventurer.\n\n",
            "The adventurer set out on a journey through enchanted forests and across vast mountains, seeking wisdom and wonder.\n\n",
            "Along the way, they encountered a wise old owl who spoke of ancient secrets hidden beneath the stars.\n\n",
            "With newfound knowledge, the adventurer returned home, forever changed by the journey.\n\nThe End."
        ];
    }

    private Task<AgentCard> GetAgentCardAsync(string agentUrl, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AgentCard>(cancellationToken);
        }

        return Task.FromResult(new AgentCard
        {
            Name = "Streaming Story Agent",
            Description = "Agent that generates stories streamed as artifact chunks, demonstrating TaskArtifactUpdateEvent with append and lastChunk semantics.",
            Url = agentUrl,
            Version = "1.0.0",
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Capabilities = new AgentCapabilities
            {
                Streaming = true,
                PushNotifications = false,
            },
            Skills =
            [
                new AgentSkill
                {
                    Id = "story-writer",
                    Name = "Story Writer",
                    Description = "Generates a short story based on a prompt, streamed in paragraph chunks.",
                    Tags = ["creative-writing", "streaming"]
                }
            ],
        });
    }
}

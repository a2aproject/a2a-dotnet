namespace A2A.UnitTests.Server;

public class TaskUpdaterTests
{
    [Fact]
    public async Task GivenTaskUpdater_WhenSubmitAsync_ThenEnqueuesTaskWithSubmittedState()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");

        await updater.SubmitAsync();

        queue.Complete();
        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.NotNull(events[0].Task);
        Assert.Equal("t1", events[0].Task!.Id);
        Assert.Equal("ctx-1", events[0].Task!.ContextId);
        Assert.Equal(TaskState.Submitted, events[0].Task!.Status.State);
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenStartWorkAsync_ThenEnqueuesWorkingStatusUpdate()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");

        await updater.StartWorkAsync();

        queue.Complete();
        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.NotNull(events[0].StatusUpdate);
        Assert.Equal(TaskState.Working, events[0].StatusUpdate!.Status.State);
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenAddArtifactAsync_ThenEnqueuesArtifactWithGeneratedId()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");

        await updater.AddArtifactAsync([Part.FromText("hello")], name: "output");

        queue.Complete();
        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.NotNull(events[0].ArtifactUpdate);
        Assert.False(string.IsNullOrEmpty(events[0].ArtifactUpdate!.Artifact.ArtifactId));
        Assert.Equal("output", events[0].ArtifactUpdate!.Artifact.Name);
        Assert.Equal("hello", events[0].ArtifactUpdate!.Artifact.Parts[0].Text);
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenAddArtifactWithExplicitId_ThenUsesProvidedId()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");

        await updater.AddArtifactAsync([Part.FromText("data")], artifactId: "custom-id");

        queue.Complete();
        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.Equal("custom-id", events[0].ArtifactUpdate!.Artifact.ArtifactId);
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenCompleteAsync_ThenEnqueuesCompletedAndCompletesQueue()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");

        await updater.CompleteAsync();

        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.Equal(TaskState.Completed, events[0].StatusUpdate!.Status.State);
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenFailAsync_ThenEnqueuesFailedAndCompletesQueue()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");

        await updater.FailAsync();

        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.Equal(TaskState.Failed, events[0].StatusUpdate!.Status.State);
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenCancelAsync_ThenEnqueuesCanceledAndCompletesQueue()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");

        await updater.CancelAsync();

        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.Equal(TaskState.Canceled, events[0].StatusUpdate!.Status.State);
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenRequireInputAsync_ThenEnqueuesInputRequiredAndCompletesQueue()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");
        var message = new Message { Role = Role.Agent, MessageId = "m1", Parts = [Part.FromText("need input")] };

        await updater.RequireInputAsync(message);

        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.Equal(TaskState.InputRequired, events[0].StatusUpdate!.Status.State);
        Assert.Equal("need input", events[0].StatusUpdate!.Status.Message!.Parts[0].Text);
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenFullLifecycle_ThenProducesCorrectEventSequence()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");

        await updater.SubmitAsync();
        await updater.StartWorkAsync();
        await updater.AddArtifactAsync([Part.FromText("result")]);
        await updater.CompleteAsync();

        var events = await CollectEventsAsync(queue);
        Assert.Equal(4, events.Count);
        Assert.NotNull(events[0].Task); // Submit
        Assert.Equal(TaskState.Working, events[1].StatusUpdate!.Status.State);
        Assert.NotNull(events[2].ArtifactUpdate); // Artifact
        Assert.Equal(TaskState.Completed, events[3].StatusUpdate!.Status.State);
    }

    private static async Task<List<StreamResponse>> CollectEventsAsync(AgentEventQueue queue)
    {
        List<StreamResponse> events = [];
        await foreach (var e in queue)
        {
            events.Add(e);
        }

        return events;
    }
}

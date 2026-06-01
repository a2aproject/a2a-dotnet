using System.Text.Json;

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

    [Fact]
    public async Task GivenTaskUpdater_WhenSubmitAsyncWithMetadata_ThenMetadataIsSetOnTask()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");
        var metadata = new Dictionary<string, JsonElement>
        {
            ["key"] = JsonDocument.Parse("\"value\"").RootElement,
        };

        await updater.SubmitAsync(metadata: metadata);

        queue.Complete();
        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.NotNull(events[0].Task);
        Assert.NotNull(events[0].Task!.Metadata);
        Assert.Equal("value", events[0].Task!.Metadata!["key"].GetString());
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenStartWorkAsyncWithMetadata_ThenMetadataIsSetOnEvent()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");
        var metadata = new Dictionary<string, JsonElement>
        {
            ["step"] = JsonDocument.Parse("1").RootElement,
        };

        await updater.StartWorkAsync(metadata: metadata);

        queue.Complete();
        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.NotNull(events[0].StatusUpdate);
        Assert.NotNull(events[0].StatusUpdate!.Metadata);
        Assert.Equal(1, events[0].StatusUpdate!.Metadata!["step"].GetInt32());
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenAddArtifactAsyncWithMetadata_ThenMetadataIsSetOnArtifact()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");
        var metadata = new Dictionary<string, JsonElement>
        {
            ["source"] = JsonDocument.Parse("\"generated\"").RootElement,
        };

        await updater.AddArtifactAsync([Part.FromText("data")], metadata: metadata);

        queue.Complete();
        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.NotNull(events[0].ArtifactUpdate);
        Assert.NotNull(events[0].ArtifactUpdate!.Artifact.Metadata);
        Assert.Equal("generated", events[0].ArtifactUpdate!.Artifact.Metadata!["source"].GetString());
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenCompleteAsyncWithMetadata_ThenMetadataIsSetOnEvent()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");
        var metadata = new Dictionary<string, JsonElement>
        {
            ["duration"] = JsonDocument.Parse("42").RootElement,
        };

        await updater.CompleteAsync(metadata: metadata);

        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.NotNull(events[0].StatusUpdate!.Metadata);
        Assert.Equal(42, events[0].StatusUpdate!.Metadata!["duration"].GetInt32());
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenFailAsyncWithMetadata_ThenMetadataIsSetOnEvent()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");
        var metadata = new Dictionary<string, JsonElement>
        {
            ["error"] = JsonDocument.Parse("\"timeout\"").RootElement,
        };

        await updater.FailAsync(metadata: metadata);

        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.NotNull(events[0].StatusUpdate!.Metadata);
        Assert.Equal("timeout", events[0].StatusUpdate!.Metadata!["error"].GetString());
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenCancelAsyncWithMetadata_ThenMetadataIsSetOnEvent()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");
        var metadata = new Dictionary<string, JsonElement>
        {
            ["reason"] = JsonDocument.Parse("\"user-requested\"").RootElement,
        };

        await updater.CancelAsync(metadata: metadata);

        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.NotNull(events[0].StatusUpdate!.Metadata);
        Assert.Equal("user-requested", events[0].StatusUpdate!.Metadata!["reason"].GetString());
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenRejectAsyncWithMetadata_ThenMetadataIsSetOnEvent()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");
        var metadata = new Dictionary<string, JsonElement>
        {
            ["policy"] = JsonDocument.Parse("\"rate-limit\"").RootElement,
        };

        await updater.RejectAsync(metadata: metadata);

        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.NotNull(events[0].StatusUpdate!.Metadata);
        Assert.Equal("rate-limit", events[0].StatusUpdate!.Metadata!["policy"].GetString());
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenRequireInputAsyncWithMetadata_ThenMetadataIsSetOnEvent()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");
        var message = new Message { Role = Role.Agent, MessageId = "m1", Parts = [Part.FromText("need input")] };
        var metadata = new Dictionary<string, JsonElement>
        {
            ["schema"] = JsonDocument.Parse("\"form-v2\"").RootElement,
        };

        await updater.RequireInputAsync(message, metadata: metadata);

        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.NotNull(events[0].StatusUpdate!.Metadata);
        Assert.Equal("form-v2", events[0].StatusUpdate!.Metadata!["schema"].GetString());
    }

    [Fact]
    public async Task GivenTaskUpdater_WhenRequireAuthAsyncWithMetadata_ThenMetadataIsSetOnEvent()
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, "t1", "ctx-1");
        var metadata = new Dictionary<string, JsonElement>
        {
            ["provider"] = JsonDocument.Parse("\"oauth2\"").RootElement,
        };

        await updater.RequireAuthAsync(metadata: metadata);

        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        Assert.NotNull(events[0].StatusUpdate!.Metadata);
        Assert.Equal("oauth2", events[0].StatusUpdate!.Metadata!["provider"].GetString());
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

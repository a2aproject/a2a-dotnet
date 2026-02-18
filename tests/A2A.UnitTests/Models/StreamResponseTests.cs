using System.Text.Json;

namespace A2A.UnitTests.Models;

public sealed class StreamResponseTests
{
    [Fact]
    public void PayloadCase_WhenTaskSet_ReturnsTask()
    {
        var response = new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx1", Status = new TaskStatus { State = TaskState.Working } }
        };

        Assert.Equal(StreamResponseCase.Task, response.PayloadCase);
    }

    [Fact]
    public void PayloadCase_WhenMessageSet_ReturnsMessage()
    {
        var response = new StreamResponse
        {
            Message = new Message { MessageId = "m1", Role = Role.Agent, Parts = [Part.FromText("hi")] }
        };

        Assert.Equal(StreamResponseCase.Message, response.PayloadCase);
    }

    [Fact]
    public void PayloadCase_WhenStatusUpdateSet_ReturnsStatusUpdate()
    {
        var response = new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx1",
                Status = new TaskStatus { State = TaskState.Working }
            }
        };

        Assert.Equal(StreamResponseCase.StatusUpdate, response.PayloadCase);
    }

    [Fact]
    public void PayloadCase_WhenArtifactUpdateSet_ReturnsArtifactUpdate()
    {
        var response = new StreamResponse
        {
            ArtifactUpdate = new TaskArtifactUpdateEvent
            {
                TaskId = "t1",
                ContextId = "ctx1",
                Artifact = new Artifact { ArtifactId = "a1", Parts = [Part.FromText("data")] }
            }
        };

        Assert.Equal(StreamResponseCase.ArtifactUpdate, response.PayloadCase);
    }

    [Fact]
    public void PayloadCase_WhenEmpty_ReturnsNone()
    {
        var response = new StreamResponse();

        Assert.Equal(StreamResponseCase.None, response.PayloadCase);
    }

    [Fact]
    public void RoundTrip_WithStatusUpdate_PreservesFields()
    {
        var response = new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = "task-99",
                ContextId = "ctx-5",
                Status = new TaskStatus { State = TaskState.InputRequired }
            }
        };

        var json = JsonSerializer.Serialize(response, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<StreamResponse>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.StatusUpdate);
        Assert.Equal("task-99", deserialized.StatusUpdate.TaskId);
        Assert.Equal("ctx-5", deserialized.StatusUpdate.ContextId);
        Assert.Equal(TaskState.InputRequired, deserialized.StatusUpdate.Status.State);
        Assert.Null(deserialized.Task);
        Assert.Null(deserialized.Message);
        Assert.Null(deserialized.ArtifactUpdate);
    }
}

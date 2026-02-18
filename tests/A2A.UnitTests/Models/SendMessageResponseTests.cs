using System.Text.Json;

namespace A2A.UnitTests.Models;

public sealed class SendMessageResponseTests
{
    [Fact]
    public void PayloadCase_WhenTaskSet_ReturnsTask()
    {
        var response = new SendMessageResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx1", Status = new TaskStatus { State = TaskState.Working } }
        };

        Assert.Equal(SendMessageResponseCase.Task, response.PayloadCase);
    }

    [Fact]
    public void PayloadCase_WhenMessageSet_ReturnsMessage()
    {
        var response = new SendMessageResponse
        {
            Message = new Message { MessageId = "m1", Role = Role.Agent, Parts = [Part.FromText("hello")] }
        };

        Assert.Equal(SendMessageResponseCase.Message, response.PayloadCase);
    }

    [Fact]
    public void PayloadCase_WhenEmpty_ReturnsNone()
    {
        var response = new SendMessageResponse();

        Assert.Equal(SendMessageResponseCase.None, response.PayloadCase);
    }

    [Fact]
    public void RoundTrip_WithTask_PreservesFields()
    {
        var response = new SendMessageResponse
        {
            Task = new AgentTask
            {
                Id = "task-42",
                ContextId = "ctx-7",
                Status = new TaskStatus { State = TaskState.Completed }
            }
        };

        var json = JsonSerializer.Serialize(response, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<SendMessageResponse>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Task);
        Assert.Equal("task-42", deserialized.Task.Id);
        Assert.Equal("ctx-7", deserialized.Task.ContextId);
        Assert.Equal(TaskState.Completed, deserialized.Task.Status.State);
        Assert.Null(deserialized.Message);
    }

    [Fact]
    public void RoundTrip_WithMessage_PreservesFields()
    {
        var response = new SendMessageResponse
        {
            Message = new Message
            {
                MessageId = "msg-1",
                Role = Role.Agent,
                Parts = [Part.FromText("response text")],
                ContextId = "ctx-1"
            }
        };

        var json = JsonSerializer.Serialize(response, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<SendMessageResponse>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Message);
        Assert.Equal("msg-1", deserialized.Message.MessageId);
        Assert.Equal(Role.Agent, deserialized.Message.Role);
        Assert.Single(deserialized.Message.Parts);
        Assert.Equal("response text", deserialized.Message.Parts[0].Text);
        Assert.Equal("ctx-1", deserialized.Message.ContextId);
        Assert.Null(deserialized.Task);
    }
}

namespace A2A.UnitTests.Server;

public class AgentContextTests
{
    [Fact]
    public void GivenMessageWithTextPart_WhenUserText_ThenReturnsText()
    {
        var context = new AgentContext
        {
            Message = new Message { MessageId = "m1", Role = Role.User, Parts = [Part.FromText("hello")] },
            TaskId = "t1",
            ContextId = "ctx-1",
            IsStreaming = false,
        };

        Assert.Equal("hello", context.UserText);
    }

    [Fact]
    public void GivenMessageWithoutTextPart_WhenUserText_ThenReturnsNull()
    {
        var context = new AgentContext
        {
            Message = new Message { MessageId = "m1", Role = Role.User, Parts = [new Part { Data = System.Text.Json.JsonDocument.Parse("{}").RootElement }] },
            TaskId = "t1",
            ContextId = "ctx-1",
            IsStreaming = false,
        };

        Assert.Null(context.UserText);
    }

    [Fact]
    public void GivenTaskSet_WhenIsContinuation_ThenReturnsTrue()
    {
        var context = new AgentContext
        {
            Message = new Message { MessageId = "m1", Role = Role.User, Parts = [Part.FromText("hi")] },
            Task = new AgentTask { Id = "t1", ContextId = "ctx-1", Status = new TaskStatus { State = TaskState.Working } },
            TaskId = "t1",
            ContextId = "ctx-1",
            IsStreaming = false,
        };

        Assert.True(context.IsContinuation);
    }

    [Fact]
    public void GivenNoTask_WhenIsContinuation_ThenReturnsFalse()
    {
        var context = new AgentContext
        {
            Message = new Message { MessageId = "m1", Role = Role.User, Parts = [Part.FromText("hi")] },
            Task = null,
            TaskId = "t1",
            ContextId = "ctx-1",
            IsStreaming = false,
        };

        Assert.False(context.IsContinuation);
    }
}

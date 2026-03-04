namespace A2A.UnitTests.Server;

public class MessageResponderTests
{
    [Fact]
    public async Task ReplyAsync_Text_SetsRoleAndContextId()
    {
        var queue = new AgentEventQueue();
        var responder = new MessageResponder(queue, "ctx-1");

        await responder.ReplyAsync("hello");

        queue.Complete();
        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        var msg = events[0].Message;
        Assert.NotNull(msg);
        Assert.Equal(Role.Agent, msg!.Role);
        Assert.False(string.IsNullOrEmpty(msg.MessageId));
        Assert.Equal("ctx-1", msg.ContextId);
        Assert.Equal("hello", msg.Parts![0].Text);
    }

    [Fact]
    public async Task ReplyAsync_Parts_SetsRoleAndContextId()
    {
        var queue = new AgentEventQueue();
        var responder = new MessageResponder(queue, "ctx-2");
        var parts = new List<Part> { Part.FromText("a"), Part.FromText("b") };

        await responder.ReplyAsync(parts);

        queue.Complete();
        var events = await CollectEventsAsync(queue);
        Assert.Single(events);
        var msg = events[0].Message;
        Assert.NotNull(msg);
        Assert.Equal(Role.Agent, msg!.Role);
        Assert.Equal("ctx-2", msg.ContextId);
        Assert.Equal(2, msg.Parts!.Count);
        Assert.Equal("a", msg.Parts[0].Text);
        Assert.Equal("b", msg.Parts[1].Text);
    }

    [Fact]
    public async Task ReplyAsync_GeneratesUniqueMessageIds()
    {
        var queue = new AgentEventQueue();
        var responder = new MessageResponder(queue, "ctx-3");

        await responder.ReplyAsync("first");
        await responder.ReplyAsync("second");

        queue.Complete();
        var events = await CollectEventsAsync(queue);
        Assert.Equal(2, events.Count);
        Assert.NotEqual(events[0].Message!.MessageId, events[1].Message!.MessageId);
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

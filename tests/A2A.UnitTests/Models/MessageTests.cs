using System.Text.Json;

namespace A2A.UnitTests.Models;

public sealed class MessageTests
{
    [Fact]
    public void Serialize_WithRequiredFields_RoundTrips()
    {
        var message = new Message
        {
            MessageId = "msg-1",
            Role = Role.User,
            Parts = [Part.FromText("Hello")]
        };

        var json = JsonSerializer.Serialize(message, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<Message>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("msg-1", deserialized.MessageId);
        Assert.Equal(Role.User, deserialized.Role);
        Assert.Single(deserialized.Parts);
        Assert.Equal("Hello", deserialized.Parts[0].Text);
    }

    [Fact]
    public void Serialize_WithAllOptionalFields_RoundTrips()
    {
        var metadata = new Dictionary<string, JsonElement>
        {
            ["key"] = JsonSerializer.SerializeToElement("value")
        };

        var message = new Message
        {
            MessageId = "msg-2",
            Role = Role.Agent,
            Parts = [Part.FromText("reply")],
            ContextId = "ctx-1",
            TaskId = "task-1",
            ReferenceTaskIds = ["ref-1", "ref-2"],
            Extensions = ["ext-1"],
            Metadata = metadata
        };

        var json = JsonSerializer.Serialize(message, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<Message>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("msg-2", deserialized.MessageId);
        Assert.Equal(Role.Agent, deserialized.Role);
        Assert.Equal("ctx-1", deserialized.ContextId);
        Assert.Equal("task-1", deserialized.TaskId);
        Assert.NotNull(deserialized.ReferenceTaskIds);
        Assert.Equal(["ref-1", "ref-2"], deserialized.ReferenceTaskIds);
        Assert.NotNull(deserialized.Extensions);
        Assert.Equal(["ext-1"], deserialized.Extensions);
        Assert.NotNull(deserialized.Metadata);
        Assert.Equal("value", deserialized.Metadata["key"].GetString());
    }

    [Fact]
    public void Deserialize_FromJson_ParsesCorrectly()
    {
        const string json = """
            {
                "messageId": "m-42",
                "role": "ROLE_USER",
                "parts": [{ "text": "test message" }],
                "contextId": "c-1",
                "taskId": "t-1"
            }
            """;

        var message = JsonSerializer.Deserialize<Message>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(message);
        Assert.Equal("m-42", message.MessageId);
        Assert.Equal(Role.User, message.Role);
        Assert.Single(message.Parts);
        Assert.Equal("test message", message.Parts[0].Text);
        Assert.Equal("c-1", message.ContextId);
        Assert.Equal("t-1", message.TaskId);
        Assert.Null(message.ReferenceTaskIds);
        Assert.Null(message.Extensions);
        Assert.Null(message.Metadata);
    }
}

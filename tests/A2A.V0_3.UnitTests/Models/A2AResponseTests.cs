using System.Text.Json;

namespace A2A.V0_3.UnitTests.Models;

public class A2AResponseTests
{
    private static readonly Dictionary<string, string> expectedMetadata = new()
    {
        ["createdAt"] = "2023-01-01T00:00:00Z"
    };

    [Fact]
    public void A2AResponse_Deserialize_Message_Succeeds()
    {
        // Arrange
        const string json = """
        {
            "kind": "message",
            "role": "user",
            "messageId": "m-2",
            "taskId": "t-2",
            "contextId": "c-2",
            "referenceTaskIds": [ "r-3", "r-4" ],
            "parts": [ { "kind": "text", "text": "hi" } ],
            "extensions": [ "foo", "bar" ],
            "metadata": {
                "createdAt": "2023-01-01T00:00:00Z"
            }
        }
        """;
        var expectedReferenceTaskIds = new[] { "r-3", "r-4" };
        var expectedParts = new[] { new TextPart { Text = "hi" } };
        var expectedExtensions = new[] { "foo", "bar" };

        // Act
        var a2aResponse = JsonSerializer.Deserialize<A2AResponse>(json, A2AJsonUtilities.DefaultOptions);
        var message = Assert.IsType<AgentMessage>(a2aResponse);

        // Assert
        Assert.Equal(MessageRole.User, message.Role);
        Assert.Equal("m-2", message.MessageId);
        Assert.Equal("t-2", message.TaskId);
        Assert.Equal("c-2", message.ContextId);
        Assert.Equal(expectedReferenceTaskIds, message.ReferenceTaskIds);
        Assert.Single(message.Parts);
        Assert.IsType<TextPart>(message.Parts[0]);
        Assert.Equal(expectedParts[0].Text, (message.Parts[0] as TextPart)!.Text);
        Assert.Equal(expectedExtensions, message.Extensions);
        Assert.NotNull(message.Metadata);
        Assert.Single(message.Metadata);
        Assert.Equal(expectedMetadata["createdAt"], message.Metadata["createdAt"].GetString());
    }

    [Fact]
    public void A2AResponse_Deserialize_AgentTask_Succeeds()
    {
        // Arrange
        const string json = """
        {
            "kind": "task",
            "id": "t-4",
            "contextId": "c-4",
            "status": { "state": "submitted" },
            "artifacts": [
                { "artifactId": "f-2", "name": "file2.txt", "description": "A text file", "parts": [] }
            ],
            "history": [
                { "kind": "message", "role": "user", "messageId": "m-4", "parts": [ { "kind": "text", "text": "go" } ] }
            ],
            "metadata": {
                "createdAt": "2023-01-01T00:00:00Z"
            }
        }
        """;
        var expectedArtifacts = new[]
        {
            new Artifact
            {
                ArtifactId = "f-2",
                Name = "file2.txt",
                Description = "A text file",
            }
        };
        var expectedHistory = new[]
        {
            new AgentMessage
            {
                Role = MessageRole.User,
                MessageId = "m-4",
            }
        };

        // Act
        var a2aResponse = JsonSerializer.Deserialize<A2AResponse>(json, A2AJsonUtilities.DefaultOptions);
        var agentTask = Assert.IsType<AgentTask>(a2aResponse);

        // Assert
        Assert.Equal("t-4", agentTask.Id);
        Assert.Equal("c-4", agentTask.ContextId);
        Assert.Equal(TaskState.Submitted, agentTask.Status.State);
        Assert.NotNull(agentTask.Artifacts);
        Assert.Single(agentTask.Artifacts);
        Assert.Equal(expectedArtifacts[0].ArtifactId, agentTask.Artifacts[0].ArtifactId);
        Assert.Equal(expectedArtifacts[0].Name, agentTask.Artifacts[0].Name);
        Assert.Equal(expectedArtifacts[0].Description, agentTask.Artifacts[0].Description);
        Assert.NotNull(agentTask.History);
        Assert.Single(agentTask.History);
        Assert.Equal(expectedHistory[0].Role, agentTask.History![0].Role);
        Assert.Equal(expectedHistory[0].MessageId, agentTask.History![0].MessageId);
        Assert.NotNull(agentTask.Metadata);
        Assert.Single(agentTask.Metadata);
        Assert.Equal(expectedMetadata["createdAt"], agentTask.Metadata["createdAt"].GetString());
    }

    [Fact]
    public void A2AResponse_Deserialize_TaskStatusUpdateEvent_Throws()
    {
        // Arrange
        const string json = """
        {
            "kind": "status-update",
            "taskId": "t-6",
            "contextId": "c-6",
            "status": { "state": "working" },
            "final": false,
            "metadata": {
                "createdAt": "2023-01-01T00:00:00Z"
            }
        }
        """;

        // Act / Assert
        var ex = Assert.Throws<A2AException>(() => JsonSerializer.Deserialize<A2AResponse>(json, A2AJsonUtilities.DefaultOptions));
        Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
    }

    [Fact]
    public void A2AResponse_Deserialize_TaskArtifactUpdateEvent_Throws()
    {
        // Arrange
        const string json = """
        {
            "kind": "artifact-update",
            "taskId": "t-8",
            "contextId": "c-8",
            "artifact": {
                "artifactId": "a-2",
                "parts": [ { "kind": "text", "text": "chunk" } ]
            },
            "append": true,
            "lastChunk": false,
            "metadata": {
                "createdAt": "2023-01-01T00:00:00Z"
            }
        }
        """;

        // Act / Assert
        var ex = Assert.Throws<A2AException>(() => JsonSerializer.Deserialize<A2AResponse>(json, A2AJsonUtilities.DefaultOptions));
        Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
    }

    [Fact]
    public void A2AResponse_Deserialize_UnknownKind_Throws()
    {
        // Arrange
        const string json = """
        {
            "kind": "unknown",
            "foo": "bar"
        }
        """;

        // Act / Assert
        var ex = Assert.Throws<A2AException>(() => JsonSerializer.Deserialize<A2AResponse>(json, A2AJsonUtilities.DefaultOptions));
        Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
    }

    [Fact]
    public void A2AResponse_Deserialize_MissingKind_Throws()
    {
        // Arrange
        const string json = """
        {
            "role": "user",
            "messageId": "m-6",
            "parts": [ { "kind": "text", "text": "hi" } ]
        }
        """;

        // Act / Assert
        var ex = Assert.Throws<A2AException>(() => JsonSerializer.Deserialize<A2AResponse>(json, A2AJsonUtilities.DefaultOptions));
        Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
    }

    [Fact]
    public void A2AResponse_Deserialize_KindNotBeingFirst_Succeeds()
    {
        // Arrange
        const string json = """
        {
            "role": "user",
            "kind": "message",
            "parts": [ { "kind": "text", "text": "hi" } ],
            "messageId": "m-7"
        }
        """;
        var expectedParts = new[] { new TextPart() { Text = "hi" } };

        // Act
        var a2aResponse = JsonSerializer.Deserialize<A2AResponse>(json, A2AJsonUtilities.DefaultOptions);
        var message = Assert.IsType<AgentMessage>(a2aResponse);

        // Assert
        Assert.Equal(MessageRole.User, message.Role);
        Assert.Equal("m-7", message.MessageId);
        Assert.Single(message.Parts);
        Assert.IsType<TextPart>(message.Parts[0]);
        Assert.Equal(expectedParts[0].Text, (message.Parts[0] as TextPart)!.Text);
    }

    [Fact]
    public void A2AResponse_Serialize_AllKnownType_Succeeds()
    {
        // Arrange
        var a2aResponses = new A2AResponse[] {
            new AgentMessage { Role = MessageRole.User, MessageId = "m-8", Parts = [new TextPart { Text = "hello" }] },
            new AgentTask { Id = "t-12", ContextId = "c-12", Status = new AgentTaskStatus { State = TaskState.Submitted, Timestamp = DateTimeOffset.Parse("2023-01-01T00:00:00+00:00", null) } }
        };
        var serializedA2aResponses = new string[] {
            "{\"kind\":\"message\",\"role\":\"user\",\"parts\":[{\"kind\":\"text\",\"text\":\"hello\"}],\"messageId\":\"m-8\"}",
            "{\"kind\":\"task\",\"id\":\"t-12\",\"contextId\":\"c-12\",\"status\":{\"state\":\"submitted\",\"timestamp\":\"2023-01-01T00:00:00+00:00\"},\"history\":[]}"
        };

        for (var i = 0; i < a2aResponses.Length; i++)
        {
            // Act
            var json = JsonSerializer.Serialize(a2aResponses[i], A2AJsonUtilities.DefaultOptions);

            // Assert
            Assert.Equal(serializedA2aResponses[i], json);
        }
    }
}

using System.Text.Json;
using Xunit.Abstractions;

namespace A2A.UnitTests.Models
{
    public sealed class A2AEventTests(ITestOutputHelper testOutput)
    {
        private static readonly Dictionary<string, string> expectedMetadata = new()
        {
            ["createdAt"] = "2023-01-01T00:00:00Z"
        };

        [Fact]
        public void A2AEvent_Deserialize_Message_Succeeds()
        {
            // Arrange
            const string json = """
            {
                "kind": "message",
                "role": "user",
                "messageId": "m-1",
                "taskId": "t-1",
                "contextId": "c-1",
                "referenceTaskIds": [ "r-1", "r-2" ],
                "parts": [ { "kind": "text", "text": "hi" } ],
                "extensions": [ "foo", "bar" ],
                "metadata": {
                    "createdAt": "2023-01-01T00:00:00Z"
                }
            }
            """;
            var expectedReferenceTaskIds = new[] { "r-1", "r-2" };
            var expectedParts = new[] { new TextPart() { Text = "hi" } };
            var expectedExtensions = new[] { "foo", "bar" };

            // Act
            var a2aEvent = JsonSerializer.Deserialize<A2AEvent>(json, A2AJsonUtilities.DefaultOptions);
            var message = Assert.IsType<AgentMessage>(a2aEvent);

            // Assert
            Assert.Equal(MessageRole.User, message.Role);
            Assert.Equal("m-1", message.MessageId);
            Assert.Equal("t-1", message.TaskId);
            Assert.Equal("c-1", message.ContextId);
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
        public void A2AEvent_Deserialize_AgentTask_Succeeds()
        {
            // Arrange
            const string json = """
            {
                "kind": "task",
                "id": "t-3",
                "contextId": "c-3",
                "status": { "state": "submitted" },
                "artifacts": [
                    { "artifactId": "f-1", "name": "file1.txt", "description": "A text file", "parts": [] }
                ],
                "history": [
                    { "kind": "message", "role": "user", "messageId": "m-3", "parts": [] }
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
                    ArtifactId = "f-1",
                    Name = "file1.txt",
                    Description = "A text file",
                }
            };
            var expectedHistory = new[]
            {
                new AgentMessage
                {
                    Role = MessageRole.User,
                    MessageId = "m-3",
                }
            };

            // Act
            var a2aEvent = JsonSerializer.Deserialize<A2AEvent>(json, A2AJsonUtilities.DefaultOptions);
            var agentTask = Assert.IsType<AgentTask>(a2aEvent);

            // Assert
            Assert.Equal("t-3", agentTask.Id);
            Assert.Equal("c-3", agentTask.ContextId);
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
        public void A2AEvent_Deserialize_TaskStatusUpdateEvent_Succeeds()
        {
            // Arrange
            const string json = """
            {
                "kind": "status-update",
                "taskId": "t-5",
                "contextId": "c-5",
                "status": { "state": "working" },
                "final": false,
                "metadata": {
                    "createdAt": "2023-01-01T00:00:00Z"
                }
            }
            """;

            // Act
            var a2aEvent = JsonSerializer.Deserialize<A2AEvent>(json, A2AJsonUtilities.DefaultOptions);
            var taskStatusUpdateEvent = Assert.IsType<TaskStatusUpdateEvent>(a2aEvent);

            // Assert
            Assert.Equal("t-5", taskStatusUpdateEvent.TaskId);
            Assert.Equal("c-5", taskStatusUpdateEvent.ContextId);
            Assert.Equal(TaskState.Working, taskStatusUpdateEvent.Status.State);
            Assert.False(taskStatusUpdateEvent.Final);
            Assert.NotNull(taskStatusUpdateEvent.Metadata);
            Assert.Single(taskStatusUpdateEvent.Metadata);
            Assert.Equal(expectedMetadata["createdAt"], taskStatusUpdateEvent.Metadata["createdAt"].GetString());
        }

        [Fact]
        public void A2AEvent_Deserialize_TaskArtifactUpdateEvent_Succeeds()
        {
            // Arrange
            const string json = """
            {
                "kind": "artifact-update",
                "taskId": "t-7",
                "contextId": "c-7",
                "artifact": {
                    "artifactId": "a-1",
                    "parts": [ { "kind": "text", "text": "chunk" } ]
                },
                "append": true,
                "lastChunk": false,
                "metadata": {
                    "createdAt": "2023-01-01T00:00:00Z"
                }
            }
            """;
            var expectedArtifact = new Artifact
            {
                ArtifactId = "a-1",
                Parts = [new TextPart { Text = "chunk" }]
            };

            // Act
            var a2aEvent = JsonSerializer.Deserialize<A2AEvent>(json, A2AJsonUtilities.DefaultOptions);
            var taskArtifactUpdateEvent = Assert.IsType<TaskArtifactUpdateEvent>(a2aEvent);

            // Assert
            Assert.Equal("t-7", taskArtifactUpdateEvent.TaskId);
            Assert.Equal("c-7", taskArtifactUpdateEvent.ContextId);
            Assert.Equal(expectedArtifact.ArtifactId, taskArtifactUpdateEvent.Artifact.ArtifactId);
            Assert.Single(taskArtifactUpdateEvent.Artifact.Parts);
            Assert.IsType<TextPart>(taskArtifactUpdateEvent.Artifact.Parts[0]);
            Assert.Equal((expectedArtifact.Parts[0] as TextPart)!.Text, (taskArtifactUpdateEvent.Artifact.Parts[0] as TextPart)!.Text);
            Assert.True(taskArtifactUpdateEvent.Append);
            Assert.False(taskArtifactUpdateEvent.LastChunk);
            Assert.NotNull(taskArtifactUpdateEvent.Metadata);
            Assert.Single(taskArtifactUpdateEvent.Metadata);
            Assert.Equal(expectedMetadata["createdAt"], taskArtifactUpdateEvent.Metadata["createdAt"].GetString());
        }

        [Fact]
        public void A2AEvent_Deserialize_UnknownKind_Throws_A2AException()
        {
            // Arrange
            const string json = """
            {
                "kind": "lorem",
                "foo": "bar"
            }
            """;

            // Act / Assert
            var ex = Assert.Throws<A2AException>(() => JsonSerializer.Deserialize<A2AEvent>(json, A2AJsonUtilities.DefaultOptions));
            Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
        }

        [Fact]
        public void A2AEvent_Deserialize_MissingKind_Throws()
        {
            // Arrange
            const string json = """
            {
                "role": "user",
                "messageId": "m-5",
                "parts": [ { "kind": "text", "text": "hi" } ]
            }
            """;

            // Act / Assert
            var ex = Assert.Throws<A2AException>(() => JsonSerializer.Deserialize<A2AEvent>(json, A2AJsonUtilities.DefaultOptions));
            Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
        }

        [Fact]
        public void A2AEvent_Deserialize_KindNotBeingFirst_Succeeds()
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
            var a2aEvent = JsonSerializer.Deserialize<A2AEvent>(json, A2AJsonUtilities.DefaultOptions);
            var message = Assert.IsType<AgentMessage>(a2aEvent);

            // Assert
            Assert.Equal(MessageRole.User, message.Role);
            Assert.Equal("m-7", message.MessageId);
            Assert.Single(message.Parts);
            Assert.IsType<TextPart>(message.Parts[0]);
            Assert.Equal(expectedParts[0].Text, (message.Parts[0] as TextPart)!.Text);
        }

        [Fact]
        public void A2AEvent_Serialize_AllKnownType_Succeeds()
        {
            // Arrange
            var a2aEvents = new A2AEvent[] {
                new AgentMessage { Role = MessageRole.User, MessageId = "m-7", Parts = [new TextPart { Text = "hello" }] },
                new AgentTask { Id = "t-9", ContextId = "c-9", Status = new AgentTaskStatus { State = TaskState.Submitted, Timestamp = DateTimeOffset.Parse("2023-01-01T00:00:00+00:00", null) } },
                new TaskStatusUpdateEvent { TaskId = "t-10", ContextId = "c-10", Status = new AgentTaskStatus { State = TaskState.Working, Timestamp = DateTimeOffset.Parse("2023-01-01T00:00:00+00:00", null) } },
                new TaskArtifactUpdateEvent { TaskId = "t-11", ContextId = "c-11" }
            };
            var serializedA2aEvents = new string[] {
                "{\"kind\":\"message\",\"role\":\"user\",\"parts\":[{\"kind\":\"text\",\"text\":\"hello\"}],\"messageId\":\"m-7\"}",
                "{\"kind\":\"task\",\"id\":\"t-9\",\"contextId\":\"c-9\",\"status\":{\"state\":\"submitted\",\"timestamp\":\"2023-01-01T00:00:00+00:00\"},\"history\":[]}",
                "{\"kind\":\"status-update\",\"status\":{\"state\":\"working\",\"timestamp\":\"2023-01-01T00:00:00+00:00\"},\"final\":false,\"taskId\":\"t-10\",\"contextId\":\"c-10\"}",
                "{\"kind\":\"artifact-update\",\"artifact\":{\"artifactId\":\"\",\"parts\":[]},\"taskId\":\"t-11\",\"contextId\":\"c-11\"}"
            };

            for (var i = 0; i < a2aEvents.Length; i++)
            {
                // Act
                var json = JsonSerializer.Serialize(a2aEvents[i], A2AJsonUtilities.DefaultOptions);

                // Assert
                Assert.Equal(serializedA2aEvents[i], json);
            }
        }

        [Theory]
        [InlineData("{ \"kind\": 1 }")]
        [InlineData("{ \"kind\": null }")]
        [InlineData("{ \"kind\": \"unknown\" }")]
        [InlineData("{ \"kind\": \"neveravaluethatsgoingtooccurinthewild\" }")]
        [InlineData("{ \"kind\": \"\" }")]
        public void A2AEvent_Deserialize_InvalidOrUnknownKind_Throws(string json)
        {
            var ex = Assert.Throws<A2AException>(() => JsonSerializer.Deserialize<A2AEvent>(json, A2AJsonUtilities.DefaultOptions));
            testOutput.WriteLine($"Exception: {ex}");

            Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
        }
    }
}

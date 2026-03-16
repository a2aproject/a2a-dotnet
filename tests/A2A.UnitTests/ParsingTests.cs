using System.Text;
using System.Text.Json;

namespace A2A.UnitTests;

public class ParsingTests
{
    [Fact]
    public void RoundTripSendMessageRequest()
    {
        // Arrange
        var sendRequest = new SendMessageRequest
        {
            Message = new Message
            {
                Parts =
                [
                    Part.FromText("Hello, World!"),
                ],
                Role = Role.User,
            },
        };
        var json = JsonSerializer.Serialize(sendRequest, A2AJsonUtilities.DefaultOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var deserializedParams = JsonSerializer.Deserialize<SendMessageRequest>(stream, A2AJsonUtilities.DefaultOptions);

        // Act
        var result = deserializedParams;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(sendRequest.Message.Parts[0].Text, result.Message.Parts[0].Text);
    }

    [Fact]
    public void JsonRpcSendMessage()
    {
        // Arrange
        var sendRequest = new SendMessageRequest
        {
            Message = new Message
            {
                Parts =
                [
                    Part.FromText("Hello, World!"),
                ],
                Role = Role.User,
            },
        };
        var jsonRpcRequest = new JsonRpcRequest
        {
            Method = A2AMethods.SendMessage,
            Params = JsonSerializer.SerializeToElement(sendRequest, A2AJsonUtilities.DefaultOptions),
        };
        var json = JsonSerializer.Serialize(jsonRpcRequest, A2AJsonUtilities.DefaultOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var deserializedRequest = JsonSerializer.Deserialize<JsonRpcRequest>(stream, A2AJsonUtilities.DefaultOptions);

        // Act
        var result = deserializedRequest?.Params?.Deserialize<SendMessageRequest>(A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(sendRequest.Message.Parts[0].Text, result.Message.Parts[0].Text);
    }

    [Fact]
    public void RoundTripTaskStatusUpdateEvent()
    {
        // Arrange
        var taskStatusUpdateEvent = new TaskStatusUpdateEvent
        {
            TaskId = "test-task",
            ContextId = "test-session",
            Status = new TaskStatus
            {
                State = TaskState.Working,
            }
        };
        var json = JsonSerializer.Serialize(taskStatusUpdateEvent, A2AJsonUtilities.DefaultOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var deserializedEvent = JsonSerializer.Deserialize<TaskStatusUpdateEvent>(stream, A2AJsonUtilities.DefaultOptions);

        // Act
        var result = deserializedEvent;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(taskStatusUpdateEvent.TaskId, result.TaskId);
        Assert.Equal(taskStatusUpdateEvent.ContextId, result.ContextId);
        Assert.Equal(taskStatusUpdateEvent.Status.State, result.Status.State);
    }

    [Fact]
    public void RoundTripArtifactUpdateEvent()
    {
        // Arrange
        var taskArtifactUpdateEvent = new TaskArtifactUpdateEvent
        {
            TaskId = "test-task",
            ContextId = "test-session",
            Artifact = new Artifact
            {
                Parts =
                [
                    Part.FromText("Hello, World!"),
                ],
            }
        };
        var json = JsonSerializer.Serialize(taskArtifactUpdateEvent, A2AJsonUtilities.DefaultOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var deserializedEvent = JsonSerializer.Deserialize<TaskArtifactUpdateEvent>(stream, A2AJsonUtilities.DefaultOptions);

        // Act
        var result = deserializedEvent;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(taskArtifactUpdateEvent.TaskId, result.TaskId);
        Assert.Equal(taskArtifactUpdateEvent.ContextId, result.ContextId);
        Assert.Equal(taskArtifactUpdateEvent.Artifact.Parts[0].Text, result.Artifact.Parts[0].Text);
    }

    [Fact]
    public void RoundTripStreamResponse()
    {
        // Arrange
        var streamResponse = new StreamResponse
        {
            ArtifactUpdate = new TaskArtifactUpdateEvent
            {
                TaskId = "test-task",
                ContextId = "test-session",
                Artifact = new Artifact
                {
                    Parts = [Part.FromText("Hello, World!")],
                }
            }
        };

        var json = JsonSerializer.Serialize(streamResponse, A2AJsonUtilities.DefaultOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var deserialized = JsonSerializer.Deserialize<StreamResponse>(stream, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.ArtifactUpdate);
        Assert.Equal("test-task", deserialized.ArtifactUpdate!.TaskId);
        Assert.Equal("Hello, World!", deserialized.ArtifactUpdate.Artifact.Parts[0].Text);
    }

    [Fact]
    public void RoundTripSendMessageResponse()
    {
        // Arrange
        var sendMsgResponse = new SendMessageResponse
        {
            Task = new AgentTask
            {
                Id = "test-task",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Completed },
            }
        };

        var json = JsonSerializer.Serialize(sendMsgResponse, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<SendMessageResponse>(json, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Task);
        Assert.Equal("test-task", deserialized.Task!.Id);
        Assert.Equal(TaskState.Completed, deserialized.Task.Status.State);
    }

    [Fact]
    public void TaskState_SerializesWithV1Format()
    {
        // Arrange
        var status = new TaskStatus { State = TaskState.Completed };

        // Act
        var json = JsonSerializer.Serialize(status, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.Contains("TASK_STATE_COMPLETED", json);
    }

    [Fact]
    public void Role_SerializesWithV1Format()
    {
        // Arrange
        var message = new Message { Role = Role.User, Parts = [] };

        // Act
        var json = JsonSerializer.Serialize(message, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.Contains("ROLE_USER", json);
    }
}
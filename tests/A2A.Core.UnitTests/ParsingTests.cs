using System.Text;
using System.Text.Json;
using A2A.Core;

namespace A2ATests;

public class ParsingTests
{

    [Fact]
    public void RoundTripTaskSendParams()
    {
        // Arrange
        var taskSendParams = new MessageSendParams
        {
            Message = new Message()
            {
                Parts =
                [
                    new TextPart()
                    {
                        Text = "Hello, World!",
                    }
                ],
            },
        };
        var json = JsonSerializer.Serialize(taskSendParams);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var deserializedParams = JsonSerializer.Deserialize<MessageSendParams>(stream);

        // Act
        var result = deserializedParams;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(((TextPart)taskSendParams.Message.Parts[0]).Text, ((TextPart)result.Message.Parts[0]).Text);
    }

    [Fact]
    public void JsonRpcTaskSend()
    {
        // Arrange
        var taskSendParams = new MessageSendParams
        {
            Message = new Message()
            {
                Parts =
                [
                    new TextPart()
                    {
                        Text = "Hello, World!",
                    }
                ],
            },
        };
        var jsonRpcRequest = new JsonRpcRequest
        {
            Method = A2AMethods.MessageSend,
            Params = JsonSerializer.SerializeToElement(taskSendParams),
        };
        var json = JsonSerializer.Serialize(jsonRpcRequest);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var deserializedRequest = JsonSerializer.Deserialize<JsonRpcRequest>(stream);

        // Act
        var result = deserializedRequest?.Params?.Deserialize<MessageSendParams>();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(((TextPart)taskSendParams.Message.Parts[0]).Text, ((TextPart)result.Message.Parts[0]).Text);
    }

    [Fact]
    public void RoundTripTaskStatusUpdateEvent()
    {
        // Arrange
        var taskStatusUpdateEvent = new TaskStatusUpdateEvent
        {
            TaskId = "test-task",
            ContextId = "test-session",
            Status = new AgentTaskStatus
            {
                State = TaskState.Working,

            }
        };
        var json = JsonSerializer.Serialize(taskStatusUpdateEvent);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var deserializedEvent = JsonSerializer.Deserialize<TaskStatusUpdateEvent>(stream);

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
                Parts = new List<Part>
                {
                    new TextPart
                    {
                        Text = "Hello, World!",
                    }
                },
            }
        };
        var json = JsonSerializer.Serialize(taskArtifactUpdateEvent);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        // Deserialize using the base class
        // This is important to ensure polymorphic deserialization works correctly
        var deserializedEvent = JsonSerializer.Deserialize<TaskArtifactUpdateEvent>(stream);

        // Act
        var result = deserializedEvent;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(taskArtifactUpdateEvent.TaskId, result.TaskId);
        Assert.Equal(taskArtifactUpdateEvent.ContextId, result.ContextId);
        Assert.Equal(taskArtifactUpdateEvent.Artifact.Parts[0].AsTextPart().Text, result.Artifact.Parts[0].AsTextPart().Text);
    }

    [Fact]
    public void RoundTripJsonRpcResponseWithArtifactUpdateStatus()
    {
        // Arrange
        var taskArtifactUpdateEvent = new TaskArtifactUpdateEvent
        {
            TaskId = "test-task",
            ContextId = "test-session",
            Artifact = new Artifact
            {
                Parts = new List<Part>
                {
                    new TextPart
                    {
                        Text = "Hello, World!",
                    }
                },
            }
        };
        var jsonRpcResponse = JsonRpcResponse.CreateJsonRpcResponse<A2AEvent>("test-id", taskArtifactUpdateEvent);
        var json = JsonSerializer.Serialize(jsonRpcResponse);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var deserializedResponse = JsonSerializer.Deserialize<JsonRpcResponse>(stream);
        // Deserialize using the base class
        // This is important to ensure polymorphic deserialization works correctly
        var resultObject = JsonSerializer.Deserialize<A2AEvent>(deserializedResponse?.Result);
        // Act


        // Assert
        Assert.NotNull(resultObject);
        var resultTaskArtifactUpdateEvent = resultObject as TaskArtifactUpdateEvent;
        Assert.Equal(taskArtifactUpdateEvent.TaskId, resultTaskArtifactUpdateEvent.TaskId);
        Assert.Equal(taskArtifactUpdateEvent.ContextId, resultTaskArtifactUpdateEvent.ContextId);
        Assert.Equal(taskArtifactUpdateEvent.Artifact.Parts[0].AsTextPart().Text, resultTaskArtifactUpdateEvent.Artifact.Parts[0].AsTextPart().Text);
    }


}
using System.Text.Json;

using V03 = A2A.V0_3;

namespace A2A.V0_3Compat.UnitTests;

public class V03TypeConverterTests
{
    // ──── Role conversion ────

    [Fact]
    public void ToV03Role_ConvertsUserRole()
    {
        var result = V03TypeConverter.ToV03Role(A2A.Role.User);
        Assert.Equal(V03.MessageRole.User, result);
    }

    [Fact]
    public void ToV03Role_ConvertsAgentRole()
    {
        var result = V03TypeConverter.ToV03Role(A2A.Role.Agent);
        Assert.Equal(V03.MessageRole.Agent, result);
    }

    [Fact]
    public void ToV1Role_ConvertsUserRole()
    {
        var result = V03TypeConverter.ToV1Role(V03.MessageRole.User);
        Assert.Equal(A2A.Role.User, result);
    }

    [Fact]
    public void ToV1Role_ConvertsAgentRole()
    {
        var result = V03TypeConverter.ToV1Role(V03.MessageRole.Agent);
        Assert.Equal(A2A.Role.Agent, result);
    }

    // ──── State conversion ────

    [Fact]
    public void ToV1State_ConvertsAllStates()
    {
        Assert.Equal(A2A.TaskState.Submitted, V03TypeConverter.ToV1State(V03.TaskState.Submitted));
        Assert.Equal(A2A.TaskState.Working, V03TypeConverter.ToV1State(V03.TaskState.Working));
        Assert.Equal(A2A.TaskState.Completed, V03TypeConverter.ToV1State(V03.TaskState.Completed));
        Assert.Equal(A2A.TaskState.Failed, V03TypeConverter.ToV1State(V03.TaskState.Failed));
        Assert.Equal(A2A.TaskState.Canceled, V03TypeConverter.ToV1State(V03.TaskState.Canceled));
        Assert.Equal(A2A.TaskState.InputRequired, V03TypeConverter.ToV1State(V03.TaskState.InputRequired));
        Assert.Equal(A2A.TaskState.Rejected, V03TypeConverter.ToV1State(V03.TaskState.Rejected));
        Assert.Equal(A2A.TaskState.AuthRequired, V03TypeConverter.ToV1State(V03.TaskState.AuthRequired));
        Assert.Equal(A2A.TaskState.Unspecified, V03TypeConverter.ToV1State(V03.TaskState.Unknown));
    }

    // ──── Part conversion: v1 → v0.3 ────

    [Fact]
    public void ToV03Part_ConvertsTextPart()
    {
        var v1Part = A2A.Part.FromText("hello");

        var v03Part = V03TypeConverter.ToV03Part(v1Part);

        var textPart = Assert.IsType<V03.TextPart>(v03Part);
        Assert.Equal("hello", textPart.Text);
    }

    [Fact]
    public void ToV03Part_ConvertsRawToBytesFilePart()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var v1Part = A2A.Part.FromRaw(bytes, "application/octet-stream", "data.bin");

        var v03Part = V03TypeConverter.ToV03Part(v1Part);

        var filePart = Assert.IsType<V03.FilePart>(v03Part);
        Assert.NotNull(filePart.File);
        Assert.Equal(Convert.ToBase64String(bytes), filePart.File.Bytes);
        Assert.Equal("application/octet-stream", filePart.File.MimeType);
        Assert.Equal("data.bin", filePart.File.Name);
    }

    [Fact]
    public void ToV03Part_ConvertsUrlToUriFilePart()
    {
        var v1Part = A2A.Part.FromUrl("https://example.com/file.pdf", "application/pdf", "file.pdf");

        var v03Part = V03TypeConverter.ToV03Part(v1Part);

        var filePart = Assert.IsType<V03.FilePart>(v03Part);
        Assert.NotNull(filePart.File);
        Assert.Equal(new Uri("https://example.com/file.pdf"), filePart.File.Uri);
        Assert.Equal("application/pdf", filePart.File.MimeType);
        Assert.Equal("file.pdf", filePart.File.Name);
    }

    [Fact]
    public void ToV03Part_ConvertsDataPart()
    {
        var json = JsonSerializer.SerializeToElement(new { key = "value" });
        var v1Part = A2A.Part.FromData(json);

        var v03Part = V03TypeConverter.ToV03Part(v1Part);

        var dataPart = Assert.IsType<V03.DataPart>(v03Part);
        Assert.True(dataPart.Data.ContainsKey("key"));
        Assert.Equal("value", dataPart.Data["key"].GetString());
    }

    // ──── Part conversion: v0.3 → v1 ────

    [Fact]
    public void ToV1Part_ConvertsTextPart()
    {
        var v03Part = new V03.TextPart { Text = "world" };

        var v1Part = V03TypeConverter.ToV1Part(v03Part);

        Assert.Equal(A2A.PartContentCase.Text, v1Part.ContentCase);
        Assert.Equal("world", v1Part.Text);
    }

    [Fact]
    public void ToV1Part_ConvertsBytesFilePartToRaw()
    {
        var bytes = new byte[] { 10, 20, 30 };
        var v03Part = new V03.FilePart
        {
            File = new V03.FileContent(Convert.ToBase64String(bytes))
            {
                MimeType = "image/png",
                Name = "image.png",
            },
        };

        var v1Part = V03TypeConverter.ToV1Part(v03Part);

        Assert.Equal(A2A.PartContentCase.Raw, v1Part.ContentCase);
        Assert.Equal(bytes, v1Part.Raw);
        Assert.Equal("image/png", v1Part.MediaType);
        Assert.Equal("image.png", v1Part.Filename);
    }

    [Fact]
    public void ToV1Part_ConvertsUriFilePartToUrl()
    {
        var v03Part = new V03.FilePart
        {
            File = new V03.FileContent(new Uri("https://example.com/doc.txt"))
            {
                MimeType = "text/plain",
                Name = "doc.txt",
            },
        };

        var v1Part = V03TypeConverter.ToV1Part(v03Part);

        Assert.Equal(A2A.PartContentCase.Url, v1Part.ContentCase);
        Assert.Equal("https://example.com/doc.txt", v1Part.Url);
        Assert.Equal("text/plain", v1Part.MediaType);
        Assert.Equal("doc.txt", v1Part.Filename);
    }

    [Fact]
    public void ToV1Part_ConvertsDataPart()
    {
        var data = new Dictionary<string, JsonElement>
        {
            ["foo"] = JsonSerializer.SerializeToElement("bar"),
        };
        var v03Part = new V03.DataPart { Data = data };

        var v1Part = V03TypeConverter.ToV1Part(v03Part);

        Assert.Equal(A2A.PartContentCase.Data, v1Part.ContentCase);
        Assert.NotNull(v1Part.Data);
        Assert.Equal(JsonValueKind.Object, v1Part.Data.Value.ValueKind);
        Assert.Equal("bar", v1Part.Data.Value.GetProperty("foo").GetString());
    }

    // ──── Message conversion ────

    [Fact]
    public void ToV03Message_ConvertsAllFields()
    {
        var v1Message = new A2A.Message
        {
            Role = A2A.Role.Agent,
            Parts = [A2A.Part.FromText("test")],
            MessageId = "msg-1",
            ContextId = "ctx-1",
            TaskId = "task-1",
            ReferenceTaskIds = ["ref-1"],
            Extensions = ["ext-1"],
        };

        var v03Message = V03TypeConverter.ToV03Message(v1Message);

        Assert.Equal(V03.MessageRole.Agent, v03Message.Role);
        Assert.Single(v03Message.Parts);
        Assert.IsType<V03.TextPart>(v03Message.Parts[0]);
        Assert.Equal("msg-1", v03Message.MessageId);
        Assert.Equal("ctx-1", v03Message.ContextId);
        Assert.Equal("task-1", v03Message.TaskId);
        Assert.Equal(["ref-1"], v03Message.ReferenceTaskIds);
        Assert.Equal(["ext-1"], v03Message.Extensions);
    }

    [Fact]
    public void ToV1Message_ConvertsAllFields()
    {
        var v03Message = new V03.AgentMessage
        {
            Role = V03.MessageRole.User,
            Parts = [new V03.TextPart { Text = "test" }],
            MessageId = "msg-2",
            ContextId = "ctx-2",
            TaskId = "task-2",
            ReferenceTaskIds = ["ref-2"],
            Extensions = ["ext-2"],
        };

        var v1Message = V03TypeConverter.ToV1Message(v03Message);

        Assert.Equal(A2A.Role.User, v1Message.Role);
        Assert.Single(v1Message.Parts);
        Assert.Equal("test", v1Message.Parts[0].Text);
        Assert.Equal("msg-2", v1Message.MessageId);
        Assert.Equal("ctx-2", v1Message.ContextId);
        Assert.Equal("task-2", v1Message.TaskId);
        Assert.Equal(["ref-2"], v1Message.ReferenceTaskIds);
        Assert.Equal(["ext-2"], v1Message.Extensions);
    }

    // ──── Task conversion ────

    [Fact]
    public void ToV1Task_ConvertsAllFields()
    {
        var v03Task = new V03.AgentTask
        {
            Id = "t-1",
            ContextId = "c-1",
            Status = new V03.AgentTaskStatus
            {
                State = V03.TaskState.Working,
                Timestamp = DateTimeOffset.Parse("2025-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            },
            History =
            [
                new V03.AgentMessage
                {
                    Role = V03.MessageRole.User,
                    Parts = [new V03.TextPart { Text = "hi" }],
                    MessageId = "h-1",
                },
            ],
            Artifacts =
            [
                new V03.Artifact
                {
                    ArtifactId = "a-1",
                    Name = "artifact",
                    Parts = [new V03.TextPart { Text = "content" }],
                },
            ],
        };

        var v1Task = V03TypeConverter.ToV1Task(v03Task);

        Assert.Equal("t-1", v1Task.Id);
        Assert.Equal("c-1", v1Task.ContextId);
        Assert.Equal(A2A.TaskState.Working, v1Task.Status.State);
        Assert.NotNull(v1Task.History);
        Assert.Single(v1Task.History);
        Assert.Equal("hi", v1Task.History[0].Parts[0].Text);
        Assert.NotNull(v1Task.Artifacts);
        Assert.Single(v1Task.Artifacts);
        Assert.Equal("a-1", v1Task.Artifacts[0].ArtifactId);
    }

    // ──── Response conversion ────

    [Fact]
    public void ToV1Response_ConvertsTaskResponse()
    {
        var v03Task = new V03.AgentTask
        {
            Id = "t-1",
            ContextId = "c-1",
            Status = new V03.AgentTaskStatus { State = V03.TaskState.Completed },
        };

        var v1Response = V03TypeConverter.ToV1Response(v03Task);

        Assert.Equal(A2A.SendMessageResponseCase.Task, v1Response.PayloadCase);
        Assert.NotNull(v1Response.Task);
        Assert.Equal("t-1", v1Response.Task.Id);
    }

    [Fact]
    public void ToV1Response_ConvertsMessageResponse()
    {
        var v03Message = new V03.AgentMessage
        {
            Role = V03.MessageRole.Agent,
            Parts = [new V03.TextPart { Text = "reply" }],
            MessageId = "msg-r",
        };

        var v1Response = V03TypeConverter.ToV1Response(v03Message);

        Assert.Equal(A2A.SendMessageResponseCase.Message, v1Response.PayloadCase);
        Assert.NotNull(v1Response.Message);
        Assert.Equal("reply", v1Response.Message.Parts[0].Text);
    }

    // ──── Stream response conversion ────

    [Fact]
    public void ToV1StreamResponse_ConvertsTaskEvent()
    {
        var v03Task = new V03.AgentTask
        {
            Id = "t-1",
            ContextId = "c-1",
            Status = new V03.AgentTaskStatus { State = V03.TaskState.Submitted },
        };

        var v1Stream = V03TypeConverter.ToV1StreamResponse(v03Task);

        Assert.Equal(A2A.StreamResponseCase.Task, v1Stream.PayloadCase);
        Assert.NotNull(v1Stream.Task);
        Assert.Equal("t-1", v1Stream.Task.Id);
    }

    [Fact]
    public void ToV1StreamResponse_ConvertsMessageEvent()
    {
        var v03Message = new V03.AgentMessage
        {
            Role = V03.MessageRole.Agent,
            Parts = [new V03.TextPart { Text = "streaming" }],
            MessageId = "msg-s",
        };

        var v1Stream = V03TypeConverter.ToV1StreamResponse(v03Message);

        Assert.Equal(A2A.StreamResponseCase.Message, v1Stream.PayloadCase);
        Assert.NotNull(v1Stream.Message);
        Assert.Equal("streaming", v1Stream.Message.Parts[0].Text);
    }

    [Fact]
    public void ToV1StreamResponse_ConvertsStatusUpdateEvent()
    {
        var v03StatusUpdate = new V03.TaskStatusUpdateEvent
        {
            TaskId = "t-2",
            ContextId = "c-2",
            Status = new V03.AgentTaskStatus
            {
                State = V03.TaskState.Working,
                Message = new V03.AgentMessage
                {
                    Role = V03.MessageRole.Agent,
                    Parts = [new V03.TextPart { Text = "working..." }],
                    MessageId = "status-msg",
                },
            },
            Final = true,
        };

        var v1Stream = V03TypeConverter.ToV1StreamResponse(v03StatusUpdate);

        Assert.Equal(A2A.StreamResponseCase.StatusUpdate, v1Stream.PayloadCase);
        Assert.NotNull(v1Stream.StatusUpdate);
        Assert.Equal("t-2", v1Stream.StatusUpdate.TaskId);
        Assert.Equal("c-2", v1Stream.StatusUpdate.ContextId);
        Assert.Equal(A2A.TaskState.Working, v1Stream.StatusUpdate.Status.State);
        Assert.NotNull(v1Stream.StatusUpdate.Status.Message);
        Assert.Equal("working...", v1Stream.StatusUpdate.Status.Message.Parts[0].Text);
    }

    [Fact]
    public void ToV1StreamResponse_ConvertsArtifactUpdateEvent()
    {
        var v03ArtifactUpdate = new V03.TaskArtifactUpdateEvent
        {
            TaskId = "t-3",
            ContextId = "c-3",
            Artifact = new V03.Artifact
            {
                ArtifactId = "art-1",
                Name = "output",
                Parts = [new V03.TextPart { Text = "artifact content" }],
            },
            Append = true,
            LastChunk = false,
        };

        var v1Stream = V03TypeConverter.ToV1StreamResponse(v03ArtifactUpdate);

        Assert.Equal(A2A.StreamResponseCase.ArtifactUpdate, v1Stream.PayloadCase);
        Assert.NotNull(v1Stream.ArtifactUpdate);
        Assert.Equal("t-3", v1Stream.ArtifactUpdate.TaskId);
        Assert.Equal("c-3", v1Stream.ArtifactUpdate.ContextId);
        Assert.Equal("art-1", v1Stream.ArtifactUpdate.Artifact.ArtifactId);
        Assert.Equal("output", v1Stream.ArtifactUpdate.Artifact.Name);
        Assert.True(v1Stream.ArtifactUpdate.Append);
        Assert.False(v1Stream.ArtifactUpdate.LastChunk);
    }

    // ──── Blocking / ReturnImmediately semantic inversion ────
    // v0.3 Blocking=true  means "server should WAIT"   (blocking)
    // v1.0 ReturnImmediately=true means "server should NOT WAIT" (non-blocking)
    // The two fields are inverses: Blocking == !ReturnImmediately

    [Fact]
    public void ToV1SendMessageRequest_BlockingTrue_SetsReturnImmediatelyFalse()
    {
        var v03Request = new V03.MessageSendParams
        {
            Message = new V03.AgentMessage { Role = V03.MessageRole.User, Parts = [] },
            Configuration = new V03.MessageSendConfiguration { Blocking = true },
        };

        var v1Request = V03TypeConverter.ToV1SendMessageRequest(v03Request);

        // Blocking=true (wait) → ReturnImmediately=false (wait)
        Assert.False(v1Request.Configuration!.ReturnImmediately);
    }

    [Fact]
    public void ToV1SendMessageRequest_BlockingFalse_SetsReturnImmediatelyTrue()
    {
        var v03Request = new V03.MessageSendParams
        {
            Message = new V03.AgentMessage { Role = V03.MessageRole.User, Parts = [] },
            Configuration = new V03.MessageSendConfiguration { Blocking = false },
        };

        var v1Request = V03TypeConverter.ToV1SendMessageRequest(v03Request);

        // Blocking=false (don't wait) → ReturnImmediately=true (don't wait)
        Assert.True(v1Request.Configuration!.ReturnImmediately);
    }

    [Fact]
    public void ToV03_ReturnImmediatelyTrue_SetsBlockingFalse()
    {
        var v1Request = new A2A.SendMessageRequest
        {
            Message = new A2A.Message { Role = A2A.Role.User, Parts = [] },
            Configuration = new A2A.SendMessageConfiguration { ReturnImmediately = true },
        };

        var v03Params = V03TypeConverter.ToV03(v1Request);

        // ReturnImmediately=true (don't wait) → Blocking=false (don't wait)
        Assert.False(v03Params.Configuration!.Blocking);
    }

    [Fact]
    public void ToV03_ReturnImmediatelyFalse_SetsBlockingTrue()
    {
        var v1Request = new A2A.SendMessageRequest
        {
            Message = new A2A.Message { Role = A2A.Role.User, Parts = [] },
            Configuration = new A2A.SendMessageConfiguration { ReturnImmediately = false },
        };

        var v03Params = V03TypeConverter.ToV03(v1Request);

        // ReturnImmediately=false (wait) → Blocking=true (wait)
        Assert.True(v03Params.Configuration!.Blocking);
    }
}

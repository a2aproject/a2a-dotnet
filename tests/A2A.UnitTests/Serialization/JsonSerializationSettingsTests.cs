using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.UnitTests.Serialization;

public sealed class JsonSerializationSettingsTests
{
    [Fact]
    public void Serialize_NullProperties_OmittedFromOutput()
    {
        var task = new AgentTask { Id = "t1", ContextId = "ctx", Status = new TaskStatus { State = TaskState.Submitted } };
        var json = JsonSerializer.Serialize(task, A2AJsonUtilities.DefaultOptions);

        Assert.DoesNotContain("\"history\"", json);
        Assert.DoesNotContain("\"artifacts\"", json);
        Assert.DoesNotContain("\"metadata\"", json);
        Assert.Contains("\"id\"", json);
        Assert.Contains("\"contextId\"", json);
    }

    [Fact]
    public void Serialize_PropertyNames_AreCamelCase()
    {
        var task = new AgentTask { Id = "t1", ContextId = "ctx", Status = new TaskStatus { State = TaskState.Submitted } };
        var json = JsonSerializer.Serialize(task, A2AJsonUtilities.DefaultOptions);

        Assert.Contains("\"contextId\"", json);
        Assert.Contains("\"id\"", json);

        var msg = new Message { MessageId = "m1", Role = Role.User, ContextId = "ctx", Parts = [Part.FromText("hi")] };
        var msgJson = JsonSerializer.Serialize(msg, A2AJsonUtilities.DefaultOptions);

        Assert.Contains("\"messageId\"", msgJson);
        Assert.Contains("\"contextId\"", msgJson);
    }

    [Theory]
    [InlineData("2026-09-02T14:55:42.7994676+00:00", "2026-09-02T14:55:42.7994676Z")]
    [InlineData("2026-09-02T10:55:42.7994676-04:00", "2026-09-02T14:55:42.7994676Z")]
    public void Serialize_Timestamp_NormalizesToUtcWithZSuffix(string timestamp, string expected)
    {
        var status = new TaskStatus
        {
            State = TaskState.Working,
            Timestamp = DateTimeOffset.Parse(timestamp, System.Globalization.CultureInfo.InvariantCulture)
        };

        var json = JsonSerializer.Serialize(status, A2AJsonUtilities.DefaultOptions);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, document.RootElement.GetProperty("timestamp").GetString());
    }

    [Fact]
    public void Deserialize_Timestamp_PreservesOffset()
    {
        const string json = """{"state":"TASK_STATE_WORKING","timestamp":"2026-09-02T10:55:42.7994676-04:00"}""";

        var status = JsonSerializer.Deserialize<TaskStatus>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(status);
        Assert.Equal(TimeSpan.FromHours(-4), status.Timestamp!.Value.Offset);
    }

    [Fact]
    public void Deserialize_NumbersFromStrings_Succeeds()
    {
        var json = """{"id":"t1","historyLength":"5"}""";
        var request = JsonSerializer.Deserialize<GetTaskRequest>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(request);
        Assert.Equal(5, request!.HistoryLength);
    }

    [Fact]
    public void RoundTrip_AgentTask_PreservesState()
    {
        var original = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx",
            Status = new TaskStatus { State = TaskState.Working, Timestamp = DateTimeOffset.UtcNow },
            History = [new Message { MessageId = "m1", Role = Role.User, Parts = [Part.FromText("hello")] }],
            Artifacts = [new Artifact { ArtifactId = "a1", Parts = [Part.FromText("result")] }]
        };

        var json = JsonSerializer.Serialize(original, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<AgentTask>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("t1", deserialized!.Id);
        Assert.Equal("ctx", deserialized.ContextId);
        Assert.Equal(TaskState.Working, deserialized.Status.State);
        Assert.Single(deserialized.History!);
        Assert.Single(deserialized.Artifacts!);
    }

    [Fact]
    public void Deserialize_DiscriminatorNotFirst_Succeeds()
    {
        // Verify that extra/out-of-order properties do not break deserialization
        var json = """{"text":"hello","kind":"text"}""";
        var part = JsonSerializer.Deserialize<Part>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(part);
        Assert.Equal("hello", part!.Text);
    }

    [Fact]
    public void DefaultOptions_HasExpectedSettings()
    {
        var opts = A2AJsonUtilities.DefaultOptions;

        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, opts.DefaultIgnoreCondition);
        Assert.Equal(JsonNumberHandling.AllowReadingFromString, opts.NumberHandling);
        Assert.True(opts.PropertyNameCaseInsensitive);
        Assert.NotNull(opts.PropertyNamingPolicy);
    }

    [Fact]
    public void Serialize_StreamResponse_OmitsNullPayloads()
    {
        var response = new StreamResponse
        {
            Task = new AgentTask { Id = "t1", ContextId = "ctx", Status = new TaskStatus { State = TaskState.Submitted } }
        };
        var json = JsonSerializer.Serialize(response, A2AJsonUtilities.DefaultOptions);

        Assert.Contains("\"task\"", json);
        Assert.DoesNotContain("\"message\"", json);
        Assert.DoesNotContain("\"statusUpdate\"", json);
        Assert.DoesNotContain("\"artifactUpdate\"", json);
    }
}

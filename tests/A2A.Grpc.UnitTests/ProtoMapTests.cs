namespace A2A.Grpc.UnitTests;

using System.Text.Json;
using A2A;
using A2A.Grpc;

/// <summary>Round-trip tests for the domain &lt;-&gt; protobuf mapping layer.</summary>
public class ProtoMapTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Part_Text_RoundTrips()
    {
        var domain = new Part { Text = "hello", Metadata = new() { ["k"] = Json("\"v\"") } };

        var result = ProtoMap.ToDomain(ProtoMap.ToProto(domain));

        Assert.Equal(PartContentCase.Text, result.ContentCase);
        Assert.Equal("hello", result.Text);
        Assert.Equal("v", result.Metadata!["k"].GetString());
    }

    [Fact]
    public void Part_Raw_RoundTripsWithMediaTypeAndFilename()
    {
        var bytes = new byte[] { 1, 2, 3, 250 };
        var domain = new Part { Raw = bytes, MediaType = "application/octet-stream", Filename = "blob.bin" };

        var result = ProtoMap.ToDomain(ProtoMap.ToProto(domain));

        Assert.Equal(PartContentCase.Raw, result.ContentCase);
        Assert.Equal(bytes, result.Raw);
        Assert.Equal("application/octet-stream", result.MediaType);
        Assert.Equal("blob.bin", result.Filename);
    }

    [Fact]
    public void Part_Url_RoundTrips()
    {
        var result = ProtoMap.ToDomain(ProtoMap.ToProto(new Part { Url = "https://example.com/a" }));

        Assert.Equal(PartContentCase.Url, result.ContentCase);
        Assert.Equal("https://example.com/a", result.Url);
    }

    [Fact]
    public void Part_Data_RoundTripsNestedJson()
    {
        var data = Json("""{"n":1,"b":true,"arr":[1,"two",null],"obj":{"x":1.5}}""");
        var result = ProtoMap.ToDomain(ProtoMap.ToProto(new Part { Data = data }));

        Assert.Equal(PartContentCase.Data, result.ContentCase);
        var value = result.Data!.Value;
        Assert.Equal(1, value.GetProperty("n").GetInt32());
        Assert.True(value.GetProperty("b").GetBoolean());
        Assert.Equal(3, value.GetProperty("arr").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("arr")[2].ValueKind);
        Assert.Equal(1.5, value.GetProperty("obj").GetProperty("x").GetDouble());
    }

    [Fact]
    public void Message_RoundTripsAllFields()
    {
        var domain = new Message
        {
            MessageId = "m1",
            Role = Role.Agent,
            ContextId = "ctx",
            TaskId = "task",
            Parts = { Part.FromText("a"), Part.FromText("b") },
            ReferenceTaskIds = ["r1", "r2"],
            Extensions = ["https://ext/1"],
            Metadata = new() { ["meta"] = Json("42") },
        };

        var result = ProtoMap.ToDomain(ProtoMap.ToProto(domain));

        Assert.Equal("m1", result.MessageId);
        Assert.Equal(Role.Agent, result.Role);
        Assert.Equal("ctx", result.ContextId);
        Assert.Equal("task", result.TaskId);
        Assert.Equal(2, result.Parts.Count);
        Assert.Equal(["r1", "r2"], result.ReferenceTaskIds);
        Assert.Equal(["https://ext/1"], result.Extensions);
        Assert.Equal(42, result.Metadata!["meta"].GetInt32());
    }

    [Fact]
    public void Message_EmptyOptionalCollections_MapToNull()
    {
        var result = ProtoMap.ToDomain(ProtoMap.ToProto(new Message { MessageId = "m", Role = Role.User, Parts = { Part.FromText("x") } }));

        Assert.Null(result.ReferenceTaskIds);
        Assert.Null(result.Extensions);
        Assert.Null(result.Metadata);
        Assert.Null(result.ContextId);
    }

    [Fact]
    public void AgentTask_RoundTripsWithStatusHistoryArtifacts()
    {
        var timestamp = new DateTimeOffset(2026, 7, 2, 10, 0, 0, TimeSpan.Zero);
        var domain = new AgentTask
        {
            Id = "t1",
            ContextId = "c1",
            Status = new TaskStatus { State = TaskState.Working, Timestamp = timestamp, Message = new Message { MessageId = "sm", Role = Role.Agent, Parts = { Part.FromText("status") } } },
            History = [new Message { MessageId = "h1", Role = Role.User, Parts = { Part.FromText("hi") } }],
            Artifacts = [new Artifact { ArtifactId = "a1", Name = "art", Parts = { Part.FromText("out") } }],
            Metadata = new() { ["k"] = Json("true") },
        };

        var result = ProtoMap.ToDomain(ProtoMap.ToProto(domain));

        Assert.Equal("t1", result.Id);
        Assert.Equal("c1", result.ContextId);
        Assert.Equal(TaskState.Working, result.Status.State);
        Assert.Equal(timestamp, result.Status.Timestamp);
        Assert.Equal("sm", result.Status.Message!.MessageId);
        Assert.Single(result.History!);
        Assert.Equal("a1", Assert.Single(result.Artifacts!).ArtifactId);
        Assert.True(result.Metadata!["k"].GetBoolean());
    }

    [Fact]
    public void TaskArtifactUpdateEvent_RoundTripsFlags()
    {
        var domain = new TaskArtifactUpdateEvent
        {
            TaskId = "t",
            ContextId = "c",
            Artifact = new Artifact { ArtifactId = "a", Parts = { Part.FromText("x") } },
            Append = true,
            LastChunk = true,
        };

        var result = ProtoMap.ToDomain(ProtoMap.ToProto(domain));

        Assert.True(result.Append);
        Assert.True(result.LastChunk);
        Assert.Equal("a", result.Artifact.ArtifactId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SendMessageResponse_RoundTripsBothCases(bool asTask)
    {
        var domain = asTask
            ? new SendMessageResponse { Task = new AgentTask { Id = "t", ContextId = "c" } }
            : new SendMessageResponse { Message = new Message { MessageId = "m", Role = Role.Agent, Parts = { Part.FromText("x") } } };

        var result = ProtoMap.ToDomain(ProtoMap.ToProto(domain));

        Assert.Equal(domain.PayloadCase, result.PayloadCase);
    }

    [Fact]
    public void StreamResponse_RoundTripsEachCase()
    {
        Assert.Equal(StreamResponseCase.Task, RoundTrip(new StreamResponse { Task = new AgentTask { Id = "t", ContextId = "c" } }));
        Assert.Equal(StreamResponseCase.Message, RoundTrip(new StreamResponse { Message = new Message { MessageId = "m", Role = Role.Agent, Parts = { Part.FromText("x") } } }));
        Assert.Equal(StreamResponseCase.StatusUpdate, RoundTrip(new StreamResponse { StatusUpdate = new TaskStatusUpdateEvent { TaskId = "t", ContextId = "c", Status = new TaskStatus { State = TaskState.Completed } } }));
        Assert.Equal(StreamResponseCase.ArtifactUpdate, RoundTrip(new StreamResponse { ArtifactUpdate = new TaskArtifactUpdateEvent { TaskId = "t", ContextId = "c", Artifact = new Artifact { ArtifactId = "a", Parts = { Part.FromText("x") } } } }));

        static StreamResponseCase RoundTrip(StreamResponse response) => ProtoMap.ToDomain(ProtoMap.ToProto(response)).PayloadCase;
    }

    [Fact]
    public void TaskPushNotificationConfig_FlattensAndRoundTrips()
    {
        var domain = new TaskPushNotificationConfig
        {
            Id = "cfg1",
            TaskId = "task1",
            Tenant = "tenantA",
            PushNotificationConfig = new PushNotificationConfig
            {
                Id = "cfg1",
                Url = "https://hook.example.com",
                Token = "tok",
                Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = "abc" },
            },
        };

        var result = ProtoMap.ToDomain(ProtoMap.ToProto(domain));

        Assert.Equal("cfg1", result.Id);
        Assert.Equal("task1", result.TaskId);
        Assert.Equal("tenantA", result.Tenant);
        Assert.Equal("https://hook.example.com", result.PushNotificationConfig.Url);
        Assert.Equal("tok", result.PushNotificationConfig.Token);
        Assert.Equal("Bearer", result.PushNotificationConfig.Authentication!.Scheme);
        Assert.Equal("abc", result.PushNotificationConfig.Authentication.Credentials);
    }

    [Fact]
    public void GetTaskRequest_HistoryLengthPresence_IsPreserved()
    {
        var withValue = ProtoMap.ToDomain(ProtoMap.ToProto(new GetTaskRequest { Id = "t", HistoryLength = 0 }));
        var withoutValue = ProtoMap.ToDomain(ProtoMap.ToProto(new GetTaskRequest { Id = "t" }));

        Assert.Equal(0, withValue.HistoryLength);
        Assert.Null(withoutValue.HistoryLength);
    }

    [Fact]
    public void ListTasksRequest_RoundTripsFiltersAndPresence()
    {
        var after = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var domain = new ListTasksRequest
        {
            ContextId = "c",
            Status = TaskState.Working,
            PageSize = 25,
            PageToken = "tok",
            HistoryLength = 3,
            StatusTimestampAfter = after,
            IncludeArtifacts = true,
        };

        var result = ProtoMap.ToDomain(ProtoMap.ToProto(domain));

        Assert.Equal("c", result.ContextId);
        Assert.Equal(TaskState.Working, result.Status);
        Assert.Equal(25, result.PageSize);
        Assert.Equal("tok", result.PageToken);
        Assert.Equal(3, result.HistoryLength);
        Assert.Equal(after, result.StatusTimestampAfter);
        Assert.True(result.IncludeArtifacts);
    }

    [Fact]
    public void ListTasksRequest_UnspecifiedStatus_MapsToNull()
    {
        var result = ProtoMap.ToDomain(ProtoMap.ToProto(new ListTasksRequest()));

        Assert.Null(result.Status);
        Assert.Null(result.PageSize);
        Assert.Null(result.IncludeArtifacts);
    }

    [Fact]
    public void CreateRequest_RoundTripsThroughProtoConfig()
    {
        var request = new CreateTaskPushNotificationConfigRequest
        {
            TaskId = "task1",
            ConfigId = "cfg1",
            Tenant = "t",
            Config = new PushNotificationConfig { Url = "https://hook", Token = "tok" },
        };

        var result = ProtoMap.ToCreateRequest(ProtoMap.ToProto(request));

        Assert.Equal("task1", result.TaskId);
        Assert.Equal("cfg1", result.ConfigId);
        Assert.Equal("https://hook", result.Config.Url);
        Assert.Equal("tok", result.Config.Token);
    }

    [Fact]
    public void AgentCard_RoundTripsCapabilitiesSkillsAndSecurity()
    {
        var domain = new AgentCard
        {
            Name = "Agent",
            Description = "desc",
            Version = "1.0.0",
            DocumentationUrl = "https://docs",
            IconUrl = "https://icon",
            SupportedInterfaces = [new AgentInterface { Url = "https://grpc", ProtocolBinding = ProtocolBindingNames.Grpc, ProtocolVersion = "1.0", Tenant = "t" }],
            Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = false, ExtendedAgentCard = true, Extensions = [new AgentExtension { Uri = "https://ext", Description = "e", Required = true, Params = Json("""{"p":1}""") }] },
            Provider = new AgentProvider { Organization = "Org", Url = "https://org" },
            Skills = [new AgentSkill { Id = "s", Name = "skill", Description = "d", Tags = ["t1"], Examples = ["ex"], SecurityRequirements = [new SecurityRequirement { Schemes = new() { ["oauth"] = new StringList { List = ["read"] } } }] }],
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["application/json"],
            SecuritySchemes = new()
            {
                ["oauth"] = new SecurityScheme
                {
                    OAuth2SecurityScheme = new OAuth2SecurityScheme
                    {
                        Description = "o",
                        OAuth2MetadataUrl = "https://meta",
                        Flows = new OAuthFlows { AuthorizationCode = new AuthorizationCodeOAuthFlow { AuthorizationUrl = "https://auth", TokenUrl = "https://token", RefreshUrl = "https://refresh", PkceRequired = true, Scopes = new() { ["read"] = "Read access" } } },
                    },
                },
            },
            Signatures = [new AgentCardSignature { Protected = "hdr", Signature = "sig", Header = new() { ["kid"] = Json("\"key1\"") } }],
        };

        var result = ProtoMap.ToDomain(ProtoMap.ToProto(domain));

        Assert.Equal("Agent", result.Name);
        Assert.Equal("https://docs", result.DocumentationUrl);
        Assert.Equal("https://icon", result.IconUrl);
        Assert.True(result.Capabilities.Streaming);
        Assert.False(result.Capabilities.PushNotifications);
        Assert.True(result.Capabilities.ExtendedAgentCard);
        Assert.Equal(1, Assert.Single(result.Capabilities.Extensions!).Params!.Value.GetProperty("p").GetInt32());
        Assert.Equal(ProtocolBindingNames.Grpc, Assert.Single(result.SupportedInterfaces).ProtocolBinding);
        Assert.Equal("Org", result.Provider!.Organization);

        var skill = Assert.Single(result.Skills);
        Assert.Equal("skill", skill.Name);
        Assert.Equal(["read"], skill.SecurityRequirements!.Single().Schemes!["oauth"].List);

        var scheme = result.SecuritySchemes!["oauth"];
        Assert.Equal(SecuritySchemeCase.OAuth2, scheme.SchemeCase);
        var flow = scheme.OAuth2SecurityScheme!.Flows.AuthorizationCode!;
        Assert.Equal("https://auth", flow.AuthorizationUrl);
        Assert.True(flow.PkceRequired);
        Assert.Equal("Read access", flow.Scopes["read"]);
        Assert.Equal("key1", Assert.Single(result.Signatures!).Header!["kid"].GetString());
    }
}

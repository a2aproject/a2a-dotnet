namespace A2A.V0_3Compat;

using System.Text.Json;

using V03 = A2A.V0_3;

/// <summary>Bidirectional type conversion between A2A v1.0 and v0.3 models.</summary>
internal static class V03TypeConverter
{
    // ──── v1.0 → v0.3 (request conversion) ────

    /// <summary>Converts a v1.0 send message request to v0.3 message send params.</summary>
    /// <param name="request">The v1.0 request to convert.</param>
    /// <returns>The converted v0.3 message send params.</returns>
    internal static V03.MessageSendParams ToV03(A2A.SendMessageRequest request)
    {
        var result = new V03.MessageSendParams
        {
            Message = ToV03Message(request.Message),
            Metadata = request.Metadata,
        };

        if (request.Configuration is { } config)
        {
            result.Configuration = new V03.MessageSendConfiguration
            {
                AcceptedOutputModes = config.AcceptedOutputModes ?? [],
                HistoryLength = config.HistoryLength,
                Blocking = config.Blocking,
            };

            if (config.PushNotificationConfig is { } pushConfig)
            {
                result.Configuration.PushNotification = ToV03PushNotificationConfig(pushConfig);
            }
        }

        return result;
    }

    /// <summary>Converts a v1.0 message to a v0.3 agent message.</summary>
    /// <param name="message">The v1.0 message to convert.</param>
    /// <returns>The converted v0.3 agent message.</returns>
    internal static V03.AgentMessage ToV03Message(A2A.Message message) =>
        new()
        {
            Role = ToV03Role(message.Role),
            Parts = message.Parts.Select(ToV03Part).ToList(),
            MessageId = message.MessageId,
            ContextId = message.ContextId,
            TaskId = message.TaskId,
            ReferenceTaskIds = message.ReferenceTaskIds,
            Extensions = message.Extensions,
            Metadata = message.Metadata,
        };

    /// <summary>Converts a v1.0 part to a v0.3 part.</summary>
    /// <param name="part">The v1.0 part to convert.</param>
    /// <returns>The converted v0.3 part.</returns>
    internal static V03.Part ToV03Part(A2A.Part part) =>
        part.ContentCase switch
        {
            PartContentCase.Text => new V03.TextPart
            {
                Text = part.Text!,
                Metadata = part.Metadata,
            },
            PartContentCase.Raw => new V03.FilePart
            {
                File = CreateV03FileContentFromBytes(Convert.ToBase64String(part.Raw!), part.MediaType, part.Filename),
                Metadata = part.Metadata,
            },
            PartContentCase.Url => new V03.FilePart
            {
                File = CreateV03FileContentFromUri(new Uri(part.Url!), part.MediaType, part.Filename),
                Metadata = part.Metadata,
            },
            PartContentCase.Data => new V03.DataPart
            {
                Data = ToV03DataDictionary(part.Data!.Value),
                Metadata = part.Metadata,
            },
            _ => new V03.TextPart { Text = string.Empty, Metadata = part.Metadata },
        };

    /// <summary>Converts a v1.0 role to a v0.3 message role.</summary>
    /// <param name="role">The v1.0 role to convert.</param>
    /// <returns>The converted v0.3 message role.</returns>
    internal static V03.MessageRole ToV03Role(A2A.Role role) =>
        role switch
        {
            Role.User => V03.MessageRole.User,
            Role.Agent => V03.MessageRole.Agent,
            _ => V03.MessageRole.User,
        };

    // ──── v0.3 → v1.0 (response conversion) ────

    /// <summary>Converts a v0.3 response to a v1.0 send message response.</summary>
    /// <param name="response">The v0.3 response to convert.</param>
    /// <returns>The converted v1.0 send message response.</returns>
    internal static A2A.SendMessageResponse ToV1Response(V03.A2AResponse response) =>
        response switch
        {
            V03.AgentTask task => new A2A.SendMessageResponse { Task = ToV1Task(task) },
            V03.AgentMessage message => new A2A.SendMessageResponse { Message = ToV1Message(message) },
            _ => throw new InvalidOperationException($"Unknown v0.3 response type: {response.GetType().Name}"),
        };

    /// <summary>Converts a v0.3 event to a v1.0 stream response.</summary>
    /// <param name="evt">The v0.3 event to convert.</param>
    /// <returns>The converted v1.0 stream response.</returns>
    internal static A2A.StreamResponse ToV1StreamResponse(V03.A2AEvent evt) =>
        evt switch
        {
            V03.AgentTask task => new A2A.StreamResponse { Task = ToV1Task(task) },
            V03.AgentMessage message => new A2A.StreamResponse { Message = ToV1Message(message) },
            V03.TaskStatusUpdateEvent statusUpdate => new A2A.StreamResponse
            {
                StatusUpdate = new A2A.TaskStatusUpdateEvent
                {
                    TaskId = statusUpdate.TaskId,
                    ContextId = statusUpdate.ContextId,
                    Status = ToV1Status(statusUpdate.Status),
                    Metadata = statusUpdate.Metadata,
                },
            },
            V03.TaskArtifactUpdateEvent artifactUpdate => new A2A.StreamResponse
            {
                ArtifactUpdate = new A2A.TaskArtifactUpdateEvent
                {
                    TaskId = artifactUpdate.TaskId,
                    ContextId = artifactUpdate.ContextId,
                    Artifact = ToV1Artifact(artifactUpdate.Artifact),
                    Append = artifactUpdate.Append ?? false,
                    LastChunk = artifactUpdate.LastChunk ?? false,
                    Metadata = artifactUpdate.Metadata,
                },
            },
            _ => throw new InvalidOperationException($"Unknown v0.3 event type: {evt.GetType().Name}"),
        };

    /// <summary>Converts a v0.3 agent task to a v1.0 agent task.</summary>
    /// <param name="task">The v0.3 task to convert.</param>
    /// <returns>The converted v1.0 agent task.</returns>
    internal static A2A.AgentTask ToV1Task(V03.AgentTask task) =>
        new()
        {
            Id = task.Id,
            ContextId = task.ContextId,
            Status = ToV1Status(task.Status),
            History = task.History?.Select(ToV1Message).ToList(),
            Artifacts = task.Artifacts?.Select(ToV1Artifact).ToList(),
            Metadata = task.Metadata,
        };

    /// <summary>Converts a v0.3 agent message to a v1.0 message.</summary>
    /// <param name="message">The v0.3 message to convert.</param>
    /// <returns>The converted v1.0 message.</returns>
    internal static A2A.Message ToV1Message(V03.AgentMessage message) =>
        new()
        {
            Role = ToV1Role(message.Role),
            Parts = message.Parts.Select(ToV1Part).ToList(),
            MessageId = message.MessageId,
            ContextId = message.ContextId,
            TaskId = message.TaskId,
            ReferenceTaskIds = message.ReferenceTaskIds,
            Extensions = message.Extensions,
            Metadata = message.Metadata,
        };

    /// <summary>Converts a v0.3 part to a v1.0 part.</summary>
    /// <param name="part">The v0.3 part to convert.</param>
    /// <returns>The converted v1.0 part.</returns>
    internal static A2A.Part ToV1Part(V03.Part part) =>
        part switch
        {
            V03.TextPart textPart => new A2A.Part
            {
                Text = textPart.Text,
                Metadata = textPart.Metadata,
            },
            V03.FilePart filePart when filePart.File.Bytes is not null => new A2A.Part
            {
                Raw = Convert.FromBase64String(filePart.File.Bytes),
                MediaType = filePart.File.MimeType,
                Filename = filePart.File.Name,
                Metadata = filePart.Metadata,
            },
            V03.FilePart filePart when filePart.File.Uri is not null => new A2A.Part
            {
                Url = filePart.File.Uri.ToString(),
                MediaType = filePart.File.MimeType,
                Filename = filePart.File.Name,
                Metadata = filePart.Metadata,
            },
            V03.FilePart => new A2A.Part { Metadata = part.Metadata },
            V03.DataPart dataPart => new A2A.Part
            {
                Data = DataDictionaryToElement(dataPart.Data),
                Metadata = dataPart.Metadata,
            },
            _ => new A2A.Part { Metadata = part.Metadata },
        };

    /// <summary>Converts a v0.3 agent task status to a v1.0 task status.</summary>
    /// <param name="status">The v0.3 status to convert.</param>
    /// <returns>The converted v1.0 task status.</returns>
    internal static A2A.TaskStatus ToV1Status(V03.AgentTaskStatus status) =>
        new()
        {
            State = ToV1State(status.State),
            Message = status.Message is not null ? ToV1Message(status.Message) : null,
            Timestamp = status.Timestamp,
        };

    /// <summary>Converts a v0.3 task state to a v1.0 task state.</summary>
    /// <param name="state">The v0.3 state to convert.</param>
    /// <returns>The converted v1.0 task state.</returns>
    internal static A2A.TaskState ToV1State(V03.TaskState state) =>
        state switch
        {
            V03.TaskState.Submitted => A2A.TaskState.Submitted,
            V03.TaskState.Working => A2A.TaskState.Working,
            V03.TaskState.Completed => A2A.TaskState.Completed,
            V03.TaskState.Failed => A2A.TaskState.Failed,
            V03.TaskState.Canceled => A2A.TaskState.Canceled,
            V03.TaskState.InputRequired => A2A.TaskState.InputRequired,
            V03.TaskState.Rejected => A2A.TaskState.Rejected,
            V03.TaskState.AuthRequired => A2A.TaskState.AuthRequired,
            _ => A2A.TaskState.Unspecified,
        };

    /// <summary>Converts a v0.3 message role to a v1.0 role.</summary>
    /// <param name="role">The v0.3 role to convert.</param>
    /// <returns>The converted v1.0 role.</returns>
    internal static A2A.Role ToV1Role(V03.MessageRole role) =>
        role switch
        {
            V03.MessageRole.User => A2A.Role.User,
            V03.MessageRole.Agent => A2A.Role.Agent,
            _ => A2A.Role.Unspecified,
        };

    /// <summary>Converts a v0.3 artifact to a v1.0 artifact.</summary>
    /// <param name="artifact">The v0.3 artifact to convert.</param>
    /// <returns>The converted v1.0 artifact.</returns>
    internal static A2A.Artifact ToV1Artifact(V03.Artifact artifact) =>
        new()
        {
            ArtifactId = artifact.ArtifactId,
            Name = artifact.Name,
            Description = artifact.Description,
            Parts = artifact.Parts.Select(ToV1Part).ToList(),
            Extensions = artifact.Extensions,
            Metadata = artifact.Metadata,
        };

    // ──── Push notification config conversion ────

    /// <summary>Converts a v1.0 push notification config to v0.3.</summary>
    /// <param name="config">The v1.0 config to convert.</param>
    /// <returns>The converted v0.3 push notification config.</returns>
    internal static V03.PushNotificationConfig ToV03PushNotificationConfig(A2A.PushNotificationConfig config)
    {
        var result = new V03.PushNotificationConfig
        {
            Url = config.Url,
            Id = config.Id,
            Token = config.Token,
        };

        if (config.Authentication is { } auth)
        {
            result.Authentication = new V03.PushNotificationAuthenticationInfo
            {
                Schemes = [auth.Scheme],
                Credentials = auth.Credentials,
            };
        }

        return result;
    }

    /// <summary>Converts a v0.3 push notification config to v1.0.</summary>
    /// <param name="config">The v0.3 config to convert.</param>
    /// <returns>The converted v1.0 push notification config.</returns>
    internal static A2A.PushNotificationConfig ToV1PushNotificationConfig(V03.PushNotificationConfig config)
    {
        var result = new A2A.PushNotificationConfig
        {
            Url = config.Url,
            Id = config.Id,
            Token = config.Token,
        };

        if (config.Authentication is { } auth && auth.Schemes.Count > 0)
        {
            result.Authentication = new A2A.AuthenticationInfo
            {
                Scheme = auth.Schemes[0],
                Credentials = auth.Credentials,
            };
        }

        return result;
    }

    /// <summary>Converts a v0.3 task push notification config to v1.0.</summary>
    /// <param name="config">The v0.3 config to convert.</param>
    /// <returns>The converted v1.0 task push notification config.</returns>
    internal static A2A.TaskPushNotificationConfig ToV1TaskPushNotificationConfig(V03.TaskPushNotificationConfig config) =>
        new()
        {
            Id = config.PushNotificationConfig.Id ?? string.Empty,
            TaskId = config.TaskId,
            PushNotificationConfig = ToV1PushNotificationConfig(config.PushNotificationConfig),
        };

    // ──── v0.3 request params → v1.0 request types (server-side compat) ────

    /// <summary>Converts v0.3 message send params to a v1.0 send message request.</summary>
    /// <param name="p">The v0.3 message send params to convert.</param>
    internal static A2A.SendMessageRequest ToV1SendMessageRequest(V03.MessageSendParams p) =>
        new()
        {
            Message = ToV1Message(p.Message),
            Configuration = p.Configuration is { } cfg ? new A2A.SendMessageConfiguration
            {
                AcceptedOutputModes = cfg.AcceptedOutputModes,
                HistoryLength = cfg.HistoryLength,
                Blocking = cfg.Blocking,
                PushNotificationConfig = cfg.PushNotification is { } pn
                    ? ToV1PushNotificationConfig(pn)
                    : null,
            } : null,
            Metadata = p.Metadata,
        };

    /// <summary>Converts v0.3 task query params to a v1.0 get task request.</summary>
    /// <param name="p">The v0.3 task query params to convert.</param>
    internal static A2A.GetTaskRequest ToV1GetTaskRequest(V03.TaskQueryParams p) =>
        new() { Id = p.Id, HistoryLength = p.HistoryLength };

    /// <summary>Converts v0.3 task ID params to a v1.0 cancel task request.</summary>
    /// <param name="p">The v0.3 task ID params to convert.</param>
    internal static A2A.CancelTaskRequest ToV1CancelTaskRequest(V03.TaskIdParams p) =>
        new() { Id = p.Id, Metadata = p.Metadata };

    // ──── v1.0 response → v0.3 (server-side compat) ────

    /// <summary>Converts a v1.0 send message response to a v0.3 A2A response.</summary>
    /// <param name="response">The v1.0 send message response to convert.</param>
    internal static V03.A2AResponse ToV03Response(A2A.SendMessageResponse response) =>
        response.PayloadCase switch
        {
            A2A.SendMessageResponseCase.Task => ToV03AgentTask(response.Task!),
            A2A.SendMessageResponseCase.Message => ToV03Message(response.Message!),
            _ => throw new InvalidOperationException($"Unknown SendMessageResponse payload case: {response.PayloadCase}"),
        };

    /// <summary>Converts a v1.0 stream response event to a v0.3 A2A event.</summary>
    /// <param name="response">The v1.0 stream response to convert.</param>
    internal static V03.A2AEvent ToV03Event(A2A.StreamResponse response) =>
        response.PayloadCase switch
        {
            A2A.StreamResponseCase.Task => ToV03AgentTask(response.Task!),
            A2A.StreamResponseCase.Message => ToV03Message(response.Message!),
            A2A.StreamResponseCase.StatusUpdate => ToV03StatusUpdate(response.StatusUpdate!),
            A2A.StreamResponseCase.ArtifactUpdate => ToV03ArtifactUpdate(response.ArtifactUpdate!),
            _ => throw new InvalidOperationException($"Unknown StreamResponse payload case: {response.PayloadCase}"),
        };

    /// <summary>Converts a v1.0 agent task to a v0.3 agent task.</summary>
    /// <param name="task">The v1.0 agent task to convert.</param>
    internal static V03.AgentTask ToV03AgentTask(A2A.AgentTask task) =>
        new()
        {
            Id = task.Id,
            ContextId = task.ContextId,
            Status = ToV03AgentTaskStatus(task.Status),
            History = task.History?.Select(ToV03Message).ToList(),
            Artifacts = task.Artifacts?.Select(ToV03Artifact).ToList(),
            Metadata = task.Metadata,
        };

    /// <summary>Converts a v1.0 task status to a v0.3 agent task status.</summary>
    /// <param name="status">The v1.0 task status to convert.</param>
    internal static V03.AgentTaskStatus ToV03AgentTaskStatus(A2A.TaskStatus status) =>
        new()
        {
            State = ToV03TaskState(status.State),
            Message = status.Message is { } msg ? ToV03Message(msg) : null,
            Timestamp = status.Timestamp ?? DateTimeOffset.UtcNow,
        };

    /// <summary>Converts a v1.0 task state to a v0.3 task state.</summary>
    /// <param name="state">The v1.0 task state to convert.</param>
    internal static V03.TaskState ToV03TaskState(A2A.TaskState state) =>
        state switch
        {
            A2A.TaskState.Submitted => V03.TaskState.Submitted,
            A2A.TaskState.Working => V03.TaskState.Working,
            A2A.TaskState.Completed => V03.TaskState.Completed,
            A2A.TaskState.Failed => V03.TaskState.Failed,
            A2A.TaskState.Canceled => V03.TaskState.Canceled,
            A2A.TaskState.InputRequired => V03.TaskState.InputRequired,
            A2A.TaskState.Rejected => V03.TaskState.Rejected,
            A2A.TaskState.AuthRequired => V03.TaskState.AuthRequired,
            _ => V03.TaskState.Unknown,
        };

    /// <summary>Converts a v1.0 artifact to a v0.3 artifact.</summary>
    /// <param name="artifact">The v1.0 artifact to convert.</param>
    internal static V03.Artifact ToV03Artifact(A2A.Artifact artifact) =>
        new()
        {
            ArtifactId = artifact.ArtifactId,
            Name = artifact.Name,
            Description = artifact.Description,
            Parts = artifact.Parts.Select(ToV03Part).ToList(),
            Extensions = artifact.Extensions,
            Metadata = artifact.Metadata,
        };

    /// <summary>Converts a v1.0 task status update event to a v0.3 event.</summary>
    /// <param name="e">The v1.0 task status update event to convert.</param>
    internal static V03.TaskStatusUpdateEvent ToV03StatusUpdate(A2A.TaskStatusUpdateEvent e) =>
        new()
        {
            TaskId = e.TaskId,
            ContextId = e.ContextId,
            Status = ToV03AgentTaskStatus(e.Status),
            Metadata = e.Metadata,
        };

    /// <summary>Converts a v1.0 task artifact update event to a v0.3 event.</summary>
    /// <param name="e">The v1.0 task artifact update event to convert.</param>
    internal static V03.TaskArtifactUpdateEvent ToV03ArtifactUpdate(A2A.TaskArtifactUpdateEvent e) =>
        new()
        {
            TaskId = e.TaskId,
            ContextId = e.ContextId,
            Artifact = ToV03Artifact(e.Artifact),
            Append = e.Append,
            LastChunk = e.LastChunk,
            Metadata = e.Metadata,
        };

    // ──── Private helpers ────

    private static V03.FileContent CreateV03FileContentFromBytes(string base64Bytes, string? mimeType, string? name) =>
        new(base64Bytes) { MimeType = mimeType, Name = name };

    private static V03.FileContent CreateV03FileContentFromUri(Uri uri, string? mimeType, string? name) =>
        new(uri) { MimeType = mimeType, Name = name };

    private static JsonElement DataDictionaryToElement(Dictionary<string, JsonElement> data)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var kvp in data)
            {
                writer.WritePropertyName(kvp.Key);
                kvp.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        var reader = new Utf8JsonReader(buffer.WrittenSpan);
        return JsonElement.ParseValue(ref reader);
    }

    private static Dictionary<string, JsonElement> ToV03DataDictionary(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var dict = new Dictionary<string, JsonElement>();
            foreach (var property in element.EnumerateObject())
            {
                dict[property.Name] = property.Value.Clone();
            }

            return dict;
        }

        return new Dictionary<string, JsonElement>
        {
            ["value"] = element.Clone(),
        };
    }
}

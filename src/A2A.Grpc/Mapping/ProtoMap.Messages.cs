namespace A2A.Grpc;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

/// <summary>
/// Message, event, request and response conversions for the core A2A operations
/// (messaging, tasks and push-notification configuration).
/// </summary>
internal static partial class ProtoMap
{
    // ---- Part ---------------------------------------------------------------------------------

    public static Protos.Part ToProto(Part part)
    {
        var result = new Protos.Part();

        switch (part.ContentCase)
        {
            case PartContentCase.Text:
                result.Text = part.Text;
                break;
            case PartContentCase.Raw:
                result.Raw = ByteString.CopyFrom(part.Raw);
                break;
            case PartContentCase.Url:
                result.Url = part.Url;
                break;
            case PartContentCase.Data:
                result.Data = ToProtoValue(part.Data!.Value);
                break;
            case PartContentCase.None:
            default:
                break;
        }

        if (part.MediaType is not null)
        {
            result.MediaType = part.MediaType;
        }

        if (part.Filename is not null)
        {
            result.Filename = part.Filename;
        }

        var metadata = ToProtoStruct(part.Metadata);
        if (metadata is not null)
        {
            result.Metadata = metadata;
        }

        return result;
    }

    public static Part ToDomain(Protos.Part part)
    {
        var result = new Part
        {
            MediaType = NullIfEmpty(part.MediaType),
            Filename = NullIfEmpty(part.Filename),
            Metadata = ToMetadata(part.Metadata),
        };

        switch (part.ContentCase)
        {
            case Protos.Part.ContentOneofCase.Text:
                result.Text = part.Text;
                break;
            case Protos.Part.ContentOneofCase.Raw:
                result.Raw = part.Raw.ToByteArray();
                break;
            case Protos.Part.ContentOneofCase.Url:
                result.Url = part.Url;
                break;
            case Protos.Part.ContentOneofCase.Data:
                result.Data = ToJsonElement(part.Data);
                break;
            case Protos.Part.ContentOneofCase.None:
            default:
                break;
        }

        return result;
    }

    // ---- Message ------------------------------------------------------------------------------

    public static Protos.Message ToProto(Message message)
    {
        var result = new Protos.Message
        {
            MessageId = message.MessageId,
            Role = (Protos.Role)(int)message.Role,
        };

        if (message.ContextId is not null)
        {
            result.ContextId = message.ContextId;
        }

        if (message.TaskId is not null)
        {
            result.TaskId = message.TaskId;
        }

        foreach (var part in message.Parts)
        {
            result.Parts.Add(ToProto(part));
        }

        if (message.ReferenceTaskIds is not null)
        {
            result.ReferenceTaskIds.AddRange(message.ReferenceTaskIds);
        }

        if (message.Extensions is not null)
        {
            result.Extensions.AddRange(message.Extensions);
        }

        var metadata = ToProtoStruct(message.Metadata);
        if (metadata is not null)
        {
            result.Metadata = metadata;
        }

        return result;
    }

    public static Message ToDomain(Protos.Message message)
    {
        var result = new Message
        {
            MessageId = message.MessageId,
            Role = (Role)(int)message.Role,
            ContextId = NullIfEmpty(message.ContextId),
            TaskId = NullIfEmpty(message.TaskId),
            Metadata = ToMetadata(message.Metadata),
        };

        foreach (var part in message.Parts)
        {
            result.Parts.Add(ToDomain(part));
        }

        if (message.ReferenceTaskIds.Count > 0)
        {
            result.ReferenceTaskIds = [.. message.ReferenceTaskIds];
        }

        if (message.Extensions.Count > 0)
        {
            result.Extensions = [.. message.Extensions];
        }

        return result;
    }

    // ---- Artifact -----------------------------------------------------------------------------

    public static Protos.Artifact ToProto(Artifact artifact)
    {
        var result = new Protos.Artifact
        {
            ArtifactId = artifact.ArtifactId,
        };

        if (artifact.Name is not null)
        {
            result.Name = artifact.Name;
        }

        if (artifact.Description is not null)
        {
            result.Description = artifact.Description;
        }

        foreach (var part in artifact.Parts)
        {
            result.Parts.Add(ToProto(part));
        }

        if (artifact.Extensions is not null)
        {
            result.Extensions.AddRange(artifact.Extensions);
        }

        var metadata = ToProtoStruct(artifact.Metadata);
        if (metadata is not null)
        {
            result.Metadata = metadata;
        }

        return result;
    }

    public static Artifact ToDomain(Protos.Artifact artifact)
    {
        var result = new Artifact
        {
            ArtifactId = artifact.ArtifactId,
            Name = NullIfEmpty(artifact.Name),
            Description = NullIfEmpty(artifact.Description),
            Metadata = ToMetadata(artifact.Metadata),
        };

        foreach (var part in artifact.Parts)
        {
            result.Parts.Add(ToDomain(part));
        }

        if (artifact.Extensions.Count > 0)
        {
            result.Extensions = [.. artifact.Extensions];
        }

        return result;
    }

    // ---- TaskStatus / Task --------------------------------------------------------------------

    public static Protos.TaskStatus ToProto(TaskStatus status)
    {
        var result = new Protos.TaskStatus
        {
            State = (Protos.TaskState)(int)status.State,
        };

        if (status.Message is not null)
        {
            result.Message = ToProto(status.Message);
        }

        if (status.Timestamp.HasValue)
        {
            result.Timestamp = Timestamp.FromDateTimeOffset(status.Timestamp.Value);
        }

        return result;
    }

    public static TaskStatus ToDomain(Protos.TaskStatus status) => new()
    {
        State = (TaskState)(int)status.State,
        Message = status.Message is null ? null : ToDomain(status.Message),
        Timestamp = status.Timestamp?.ToDateTimeOffset(),
    };

    public static Protos.Task ToProto(AgentTask task)
    {
        var result = new Protos.Task
        {
            Id = task.Id,
            ContextId = task.ContextId,
            Status = ToProto(task.Status),
        };

        if (task.History is not null)
        {
            foreach (var message in task.History)
            {
                result.History.Add(ToProto(message));
            }
        }

        if (task.Artifacts is not null)
        {
            foreach (var artifact in task.Artifacts)
            {
                result.Artifacts.Add(ToProto(artifact));
            }
        }

        var metadata = ToProtoStruct(task.Metadata);
        if (metadata is not null)
        {
            result.Metadata = metadata;
        }

        return result;
    }

    public static AgentTask ToDomain(Protos.Task task)
    {
        var result = new AgentTask
        {
            Id = task.Id,
            ContextId = task.ContextId,
            Status = ToDomain(task.Status),
            Metadata = ToMetadata(task.Metadata),
        };

        if (task.History.Count > 0)
        {
            result.History = [];
            foreach (var message in task.History)
            {
                result.History.Add(ToDomain(message));
            }
        }

        if (task.Artifacts.Count > 0)
        {
            result.Artifacts = [];
            foreach (var artifact in task.Artifacts)
            {
                result.Artifacts.Add(ToDomain(artifact));
            }
        }

        return result;
    }

    // ---- Stream events ------------------------------------------------------------------------

    public static Protos.TaskStatusUpdateEvent ToProto(TaskStatusUpdateEvent update)
    {
        var result = new Protos.TaskStatusUpdateEvent
        {
            TaskId = update.TaskId,
            ContextId = update.ContextId,
            Status = ToProto(update.Status),
        };

        var metadata = ToProtoStruct(update.Metadata);
        if (metadata is not null)
        {
            result.Metadata = metadata;
        }

        return result;
    }

    public static TaskStatusUpdateEvent ToDomain(Protos.TaskStatusUpdateEvent update) => new()
    {
        TaskId = update.TaskId,
        ContextId = update.ContextId,
        Status = ToDomain(update.Status),
        Metadata = ToMetadata(update.Metadata),
    };

    public static Protos.TaskArtifactUpdateEvent ToProto(TaskArtifactUpdateEvent update)
    {
        var result = new Protos.TaskArtifactUpdateEvent
        {
            TaskId = update.TaskId,
            ContextId = update.ContextId,
            Artifact = ToProto(update.Artifact),
            Append = update.Append,
            LastChunk = update.LastChunk,
        };

        var metadata = ToProtoStruct(update.Metadata);
        if (metadata is not null)
        {
            result.Metadata = metadata;
        }

        return result;
    }

    public static TaskArtifactUpdateEvent ToDomain(Protos.TaskArtifactUpdateEvent update) => new()
    {
        TaskId = update.TaskId,
        ContextId = update.ContextId,
        Artifact = ToDomain(update.Artifact),
        Append = update.Append,
        LastChunk = update.LastChunk,
        Metadata = ToMetadata(update.Metadata),
    };

    // ---- SendMessageResponse / StreamResponse -------------------------------------------------

    public static Protos.SendMessageResponse ToProto(SendMessageResponse response)
    {
        var result = new Protos.SendMessageResponse();
        switch (response.PayloadCase)
        {
            case SendMessageResponseCase.Task:
                result.Task = ToProto(response.Task!);
                break;
            case SendMessageResponseCase.Message:
                result.Message = ToProto(response.Message!);
                break;
            case SendMessageResponseCase.None:
            default:
                break;
        }

        return result;
    }

    public static SendMessageResponse ToDomain(Protos.SendMessageResponse response) => response.PayloadCase switch
    {
        Protos.SendMessageResponse.PayloadOneofCase.Task => new SendMessageResponse { Task = ToDomain(response.Task) },
        Protos.SendMessageResponse.PayloadOneofCase.Message => new SendMessageResponse { Message = ToDomain(response.Message) },
        _ => new SendMessageResponse(),
    };

    public static Protos.StreamResponse ToProto(StreamResponse response)
    {
        var result = new Protos.StreamResponse();
        switch (response.PayloadCase)
        {
            case StreamResponseCase.Task:
                result.Task = ToProto(response.Task!);
                break;
            case StreamResponseCase.Message:
                result.Message = ToProto(response.Message!);
                break;
            case StreamResponseCase.StatusUpdate:
                result.StatusUpdate = ToProto(response.StatusUpdate!);
                break;
            case StreamResponseCase.ArtifactUpdate:
                result.ArtifactUpdate = ToProto(response.ArtifactUpdate!);
                break;
            case StreamResponseCase.None:
            default:
                break;
        }

        return result;
    }

    public static StreamResponse ToDomain(Protos.StreamResponse response) => response.PayloadCase switch
    {
        Protos.StreamResponse.PayloadOneofCase.Task => new StreamResponse { Task = ToDomain(response.Task) },
        Protos.StreamResponse.PayloadOneofCase.Message => new StreamResponse { Message = ToDomain(response.Message) },
        Protos.StreamResponse.PayloadOneofCase.StatusUpdate => new StreamResponse { StatusUpdate = ToDomain(response.StatusUpdate) },
        Protos.StreamResponse.PayloadOneofCase.ArtifactUpdate => new StreamResponse { ArtifactUpdate = ToDomain(response.ArtifactUpdate) },
        _ => new StreamResponse(),
    };

    // ---- Push notification configuration ------------------------------------------------------
    // The proto flattens PushNotificationConfig into TaskPushNotificationConfig; the domain nests it.

    public static Protos.TaskPushNotificationConfig ToProto(TaskPushNotificationConfig config) =>
        ToProtoPushConfig(config.PushNotificationConfig, config.Id, config.TaskId, config.Tenant);

    private static Protos.TaskPushNotificationConfig ToProtoPushConfig(PushNotificationConfig config, string? id, string taskId, string? tenant)
    {
        var result = new Protos.TaskPushNotificationConfig
        {
            TaskId = taskId,
            Url = config.Url,
        };

        if (id is not null)
        {
            result.Id = id;
        }

        if (tenant is not null)
        {
            result.Tenant = tenant;
        }

        if (config.Token is not null)
        {
            result.Token = config.Token;
        }

        if (config.Authentication is not null)
        {
            result.Authentication = ToProto(config.Authentication);
        }

        return result;
    }

    public static TaskPushNotificationConfig ToDomain(Protos.TaskPushNotificationConfig config) => new()
    {
        Id = config.Id,
        TaskId = config.TaskId,
        Tenant = NullIfEmpty(config.Tenant),
        PushNotificationConfig = new PushNotificationConfig
        {
            Id = NullIfEmpty(config.Id),
            Url = config.Url,
            Token = NullIfEmpty(config.Token),
            Authentication = config.Authentication is null ? null : ToDomain(config.Authentication),
        },
    };

    public static Protos.AuthenticationInfo ToProto(AuthenticationInfo info)
    {
        var result = new Protos.AuthenticationInfo
        {
            Scheme = info.Scheme,
        };

        if (info.Credentials is not null)
        {
            result.Credentials = info.Credentials;
        }

        return result;
    }

    public static AuthenticationInfo ToDomain(Protos.AuthenticationInfo info) => new()
    {
        Scheme = info.Scheme,
        Credentials = NullIfEmpty(info.Credentials),
    };

    // ---- SendMessageConfiguration -------------------------------------------------------------

    public static Protos.SendMessageConfiguration ToProto(SendMessageConfiguration configuration)
    {
        var result = new Protos.SendMessageConfiguration
        {
            ReturnImmediately = configuration.ReturnImmediately,
        };

        if (configuration.AcceptedOutputModes is not null)
        {
            result.AcceptedOutputModes.AddRange(configuration.AcceptedOutputModes);
        }

        if (configuration.HistoryLength.HasValue)
        {
            result.HistoryLength = configuration.HistoryLength.Value;
        }

        if (configuration.PushNotificationConfig is not null)
        {
            // task_id is left empty per spec when sent inside a SendMessage request.
            result.TaskPushNotificationConfig = ToProtoPushConfig(
                configuration.PushNotificationConfig,
                configuration.PushNotificationConfig.Id,
                taskId: string.Empty,
                tenant: null);
        }

        return result;
    }

    public static SendMessageConfiguration ToDomain(Protos.SendMessageConfiguration configuration)
    {
        var result = new SendMessageConfiguration
        {
            ReturnImmediately = configuration.ReturnImmediately,
            HistoryLength = configuration.HasHistoryLength ? configuration.HistoryLength : null,
        };

        if (configuration.AcceptedOutputModes.Count > 0)
        {
            result.AcceptedOutputModes = [.. configuration.AcceptedOutputModes];
        }

        if (configuration.TaskPushNotificationConfig is not null)
        {
            result.PushNotificationConfig = ToDomain(configuration.TaskPushNotificationConfig).PushNotificationConfig;
        }

        return result;
    }

    // ---- Requests -----------------------------------------------------------------------------

    public static Protos.SendMessageRequest ToProto(SendMessageRequest request)
    {
        var result = new Protos.SendMessageRequest
        {
            Message = ToProto(request.Message),
        };

        if (request.Tenant is not null)
        {
            result.Tenant = request.Tenant;
        }

        if (request.Configuration is not null)
        {
            result.Configuration = ToProto(request.Configuration);
        }

        var metadata = ToProtoStruct(request.Metadata);
        if (metadata is not null)
        {
            result.Metadata = metadata;
        }

        return result;
    }

    public static SendMessageRequest ToDomain(Protos.SendMessageRequest request) => new()
    {
        Tenant = NullIfEmpty(request.Tenant),
        Message = ToDomain(request.Message),
        Configuration = request.Configuration is null ? null : ToDomain(request.Configuration),
        Metadata = ToMetadata(request.Metadata),
    };

    public static Protos.GetTaskRequest ToProto(GetTaskRequest request)
    {
        var result = new Protos.GetTaskRequest
        {
            Id = request.Id,
        };

        if (request.Tenant is not null)
        {
            result.Tenant = request.Tenant;
        }

        if (request.HistoryLength.HasValue)
        {
            result.HistoryLength = request.HistoryLength.Value;
        }

        return result;
    }

    public static GetTaskRequest ToDomain(Protos.GetTaskRequest request) => new()
    {
        Tenant = NullIfEmpty(request.Tenant),
        Id = request.Id,
        HistoryLength = request.HasHistoryLength ? request.HistoryLength : null,
    };

    public static Protos.ListTasksRequest ToProto(ListTasksRequest request)
    {
        var result = new Protos.ListTasksRequest
        {
            Status = (Protos.TaskState)(int)(request.Status ?? TaskState.Unspecified),
        };

        if (request.Tenant is not null)
        {
            result.Tenant = request.Tenant;
        }

        if (request.ContextId is not null)
        {
            result.ContextId = request.ContextId;
        }

        if (request.PageSize.HasValue)
        {
            result.PageSize = request.PageSize.Value;
        }

        if (request.PageToken is not null)
        {
            result.PageToken = request.PageToken;
        }

        if (request.HistoryLength.HasValue)
        {
            result.HistoryLength = request.HistoryLength.Value;
        }

        if (request.StatusTimestampAfter.HasValue)
        {
            result.StatusTimestampAfter = Timestamp.FromDateTimeOffset(request.StatusTimestampAfter.Value);
        }

        if (request.IncludeArtifacts.HasValue)
        {
            result.IncludeArtifacts = request.IncludeArtifacts.Value;
        }

        return result;
    }

    public static ListTasksRequest ToDomain(Protos.ListTasksRequest request) => new()
    {
        Tenant = NullIfEmpty(request.Tenant),
        ContextId = NullIfEmpty(request.ContextId),
        Status = request.Status == Protos.TaskState.Unspecified ? null : (TaskState)(int)request.Status,
        PageSize = request.HasPageSize ? request.PageSize : null,
        PageToken = NullIfEmpty(request.PageToken),
        HistoryLength = request.HasHistoryLength ? request.HistoryLength : null,
        StatusTimestampAfter = request.StatusTimestampAfter?.ToDateTimeOffset(),
        IncludeArtifacts = request.HasIncludeArtifacts ? request.IncludeArtifacts : null,
    };

    public static Protos.CancelTaskRequest ToProto(CancelTaskRequest request)
    {
        var result = new Protos.CancelTaskRequest
        {
            Id = request.Id,
        };

        if (request.Tenant is not null)
        {
            result.Tenant = request.Tenant;
        }

        var metadata = ToProtoStruct(request.Metadata);
        if (metadata is not null)
        {
            result.Metadata = metadata;
        }

        return result;
    }

    public static CancelTaskRequest ToDomain(Protos.CancelTaskRequest request) => new()
    {
        Tenant = NullIfEmpty(request.Tenant),
        Id = request.Id,
        Metadata = ToMetadata(request.Metadata),
    };

    public static Protos.SubscribeToTaskRequest ToProto(SubscribeToTaskRequest request)
    {
        var result = new Protos.SubscribeToTaskRequest
        {
            Id = request.Id,
        };

        if (request.Tenant is not null)
        {
            result.Tenant = request.Tenant;
        }

        return result;
    }

    public static SubscribeToTaskRequest ToDomain(Protos.SubscribeToTaskRequest request) => new()
    {
        Tenant = NullIfEmpty(request.Tenant),
        Id = request.Id,
    };

    public static Protos.GetTaskPushNotificationConfigRequest ToProto(GetTaskPushNotificationConfigRequest request)
    {
        var result = new Protos.GetTaskPushNotificationConfigRequest
        {
            Id = request.Id,
            TaskId = request.TaskId,
        };

        if (request.Tenant is not null)
        {
            result.Tenant = request.Tenant;
        }

        return result;
    }

    public static GetTaskPushNotificationConfigRequest ToDomain(Protos.GetTaskPushNotificationConfigRequest request) => new()
    {
        Tenant = NullIfEmpty(request.Tenant),
        Id = request.Id,
        TaskId = request.TaskId,
    };

    public static Protos.DeleteTaskPushNotificationConfigRequest ToProto(DeleteTaskPushNotificationConfigRequest request)
    {
        var result = new Protos.DeleteTaskPushNotificationConfigRequest
        {
            Id = request.Id,
            TaskId = request.TaskId,
        };

        if (request.Tenant is not null)
        {
            result.Tenant = request.Tenant;
        }

        return result;
    }

    public static DeleteTaskPushNotificationConfigRequest ToDomain(Protos.DeleteTaskPushNotificationConfigRequest request) => new()
    {
        Tenant = NullIfEmpty(request.Tenant),
        Id = request.Id,
        TaskId = request.TaskId,
    };

    public static Protos.ListTaskPushNotificationConfigsRequest ToProto(ListTaskPushNotificationConfigRequest request)
    {
        var result = new Protos.ListTaskPushNotificationConfigsRequest
        {
            TaskId = request.TaskId,
        };

        if (request.Tenant is not null)
        {
            result.Tenant = request.Tenant;
        }

        if (request.PageSize.HasValue)
        {
            result.PageSize = request.PageSize.Value;
        }

        if (request.PageToken is not null)
        {
            result.PageToken = request.PageToken;
        }

        return result;
    }

    public static ListTaskPushNotificationConfigRequest ToDomain(Protos.ListTaskPushNotificationConfigsRequest request) => new()
    {
        Tenant = NullIfEmpty(request.Tenant),
        TaskId = request.TaskId,
        PageSize = request.PageSize == 0 ? null : request.PageSize,
        PageToken = NullIfEmpty(request.PageToken),
    };

    public static Protos.GetExtendedAgentCardRequest ToProto(GetExtendedAgentCardRequest request)
    {
        var result = new Protos.GetExtendedAgentCardRequest();
        if (request.Tenant is not null)
        {
            result.Tenant = request.Tenant;
        }

        return result;
    }

    public static GetExtendedAgentCardRequest ToDomain(Protos.GetExtendedAgentCardRequest request) => new()
    {
        Tenant = NullIfEmpty(request.Tenant),
    };

    // Builds the proto push-config message for a CreateTaskPushNotificationConfig call.
    public static Protos.TaskPushNotificationConfig ToProto(CreateTaskPushNotificationConfigRequest request) => ToProto(new TaskPushNotificationConfig
    {
        Id = request.ConfigId,
        TaskId = request.TaskId,
        Tenant = request.Tenant,
        PushNotificationConfig = request.Config,
    });

    // Reconstructs a CreateTaskPushNotificationConfigRequest from the proto push-config message.
    public static CreateTaskPushNotificationConfigRequest ToCreateRequest(Protos.TaskPushNotificationConfig config) => new()
    {
        Tenant = NullIfEmpty(config.Tenant),
        TaskId = config.TaskId,
        ConfigId = config.Id,
        Config = ToDomain(config).PushNotificationConfig,
    };

    // ---- Responses ----------------------------------------------------------------------------

    public static Protos.ListTasksResponse ToProto(ListTasksResponse response)
    {
        var result = new Protos.ListTasksResponse
        {
            NextPageToken = response.NextPageToken,
            PageSize = response.PageSize,
            TotalSize = response.TotalSize,
        };

        foreach (var task in response.Tasks)
        {
            result.Tasks.Add(ToProto(task));
        }

        return result;
    }

    public static ListTasksResponse ToDomain(Protos.ListTasksResponse response)
    {
        var result = new ListTasksResponse
        {
            NextPageToken = response.NextPageToken,
            PageSize = response.PageSize,
            TotalSize = response.TotalSize,
        };

        foreach (var task in response.Tasks)
        {
            result.Tasks.Add(ToDomain(task));
        }

        return result;
    }

    public static Protos.ListTaskPushNotificationConfigsResponse ToProto(ListTaskPushNotificationConfigResponse response)
    {
        var result = new Protos.ListTaskPushNotificationConfigsResponse
        {
            NextPageToken = response.NextPageToken,
        };

        if (response.Configs is not null)
        {
            foreach (var config in response.Configs)
            {
                result.Configs.Add(ToProto(config));
            }
        }

        return result;
    }

    public static ListTaskPushNotificationConfigResponse ToDomain(Protos.ListTaskPushNotificationConfigsResponse response)
    {
        var result = new ListTaskPushNotificationConfigResponse
        {
            NextPageToken = NullIfEmpty(response.NextPageToken),
        };

        if (response.Configs.Count > 0)
        {
            result.Configs = [];
            foreach (var config in response.Configs)
            {
                result.Configs.Add(ToDomain(config));
            }
        }

        return result;
    }
}

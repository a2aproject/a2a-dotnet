using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>Represents an empty operation result.</summary>
[JsonConverter(typeof(A2AEmptyResultJsonConverter))]
public sealed class A2AEmptyResult
{
    private A2AEmptyResult()
    {
    }

    /// <summary>Gets the singleton empty result instance.</summary>
    public static A2AEmptyResult Instance { get; } = new();
}

internal sealed class A2AEmptyResultJsonConverter : JsonConverter<A2AEmptyResult>
{
    public override A2AEmptyResult Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException();
        }

        return A2AEmptyResult.Instance;
    }

    public override void Write(
        Utf8JsonWriter writer,
        A2AEmptyResult value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteEndObject();
    }
}

/// <summary>Provides typed handles for the built-in A2A operations.</summary>
public sealed class A2AStandardOperations
{
    internal A2AStandardOperations(
        A2AOperation<SendMessageRequest, SendMessageResponse> sendMessage,
        A2AStreamingOperation<SendMessageRequest, StreamResponse> sendStreamingMessage,
        A2AOperation<GetTaskRequest, AgentTask> getTask,
        A2AOperation<ListTasksRequest, ListTasksResponse> listTasks,
        A2AOperation<CancelTaskRequest, AgentTask> cancelTask,
        A2AStreamingOperation<SubscribeToTaskRequest, StreamResponse> subscribeToTask,
        A2AOperation<TaskPushNotificationConfig, TaskPushNotificationConfig>
            createTaskPushNotificationConfig,
        A2AOperation<GetTaskPushNotificationConfigRequest, TaskPushNotificationConfig>
            getTaskPushNotificationConfig,
        A2AOperation<
            ListTaskPushNotificationConfigsRequest,
            ListTaskPushNotificationConfigsResponse> listTaskPushNotificationConfigs,
        A2AOperation<
            DeleteTaskPushNotificationConfigRequest,
            A2AEmptyResult> deleteTaskPushNotificationConfig,
        A2AOperation<GetExtendedAgentCardRequest, AgentCard> getExtendedAgentCard)
    {
        SendMessage = sendMessage;
        SendStreamingMessage = sendStreamingMessage;
        GetTask = getTask;
        ListTasks = listTasks;
        CancelTask = cancelTask;
        SubscribeToTask = subscribeToTask;
        CreateTaskPushNotificationConfig = createTaskPushNotificationConfig;
        GetTaskPushNotificationConfig = getTaskPushNotificationConfig;
        ListTaskPushNotificationConfigs = listTaskPushNotificationConfigs;
        DeleteTaskPushNotificationConfig = deleteTaskPushNotificationConfig;
        GetExtendedAgentCard = getExtendedAgentCard;
    }

    /// <summary>Gets the standard send-message operation.</summary>
    public A2AOperation<SendMessageRequest, SendMessageResponse> SendMessage { get; }

    /// <summary>Gets the standard send-streaming-message operation.</summary>
    public A2AStreamingOperation<SendMessageRequest, StreamResponse>
        SendStreamingMessage { get; }

    /// <summary>Gets the standard get-task operation.</summary>
    public A2AOperation<GetTaskRequest, AgentTask> GetTask { get; }

    /// <summary>Gets the standard list-tasks operation.</summary>
    public A2AOperation<ListTasksRequest, ListTasksResponse> ListTasks { get; }

    /// <summary>Gets the standard cancel-task operation.</summary>
    public A2AOperation<CancelTaskRequest, AgentTask> CancelTask { get; }

    /// <summary>Gets the standard subscribe-to-task operation.</summary>
    public A2AStreamingOperation<SubscribeToTaskRequest, StreamResponse>
        SubscribeToTask { get; }

    /// <summary>Gets the standard create-task-push-notification-config operation.</summary>
    public A2AOperation<TaskPushNotificationConfig, TaskPushNotificationConfig>
        CreateTaskPushNotificationConfig { get; }

    /// <summary>Gets the standard get-task-push-notification-config operation.</summary>
    public A2AOperation<GetTaskPushNotificationConfigRequest, TaskPushNotificationConfig>
        GetTaskPushNotificationConfig { get; }

    /// <summary>Gets the standard list-task-push-notification-configs operation.</summary>
    public A2AOperation<
        ListTaskPushNotificationConfigsRequest,
        ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigs
    {
        get;
    }

    /// <summary>Gets the standard delete-task-push-notification-config operation.</summary>
    public A2AOperation<DeleteTaskPushNotificationConfigRequest, A2AEmptyResult>
        DeleteTaskPushNotificationConfig { get; }

    /// <summary>Gets the standard get-extended-agent-card operation.</summary>
    public A2AOperation<GetExtendedAgentCardRequest, AgentCard> GetExtendedAgentCard
    {
        get;
    }
}

/// <summary>Registers the standard A2A operations in an operation catalog builder.</summary>
public static class A2AStandardOperationCatalogBuilderExtensions
{
    /// <summary>Adds the standard A2A operations to the builder.</summary>
    /// <param name="builder">The operation catalog builder.</param>
    /// <returns>The typed standard operation handles.</returns>
    public static A2AStandardOperations AddStandardA2AOperations(
        this A2AOperationCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return new A2AStandardOperations(
            builder.DefineStandardUnary<SendMessageRequest, SendMessageResponse>(
                new A2AOperationId("https://a2a-protocol.org/operations/send-message"),
                ValidateSendMessage),
            builder.DefineStandardStreaming<SendMessageRequest, StreamResponse>(
                new A2AOperationId("https://a2a-protocol.org/operations/send-message-stream"),
                ValidateSendMessage),
            builder.DefineStandardUnary<GetTaskRequest, AgentTask>(
                new A2AOperationId("https://a2a-protocol.org/operations/get-task"),
                static request => ValidateHistoryLength(request.HistoryLength)),
            builder.DefineStandardUnary<ListTasksRequest, ListTasksResponse>(
                new A2AOperationId("https://a2a-protocol.org/operations/list-tasks"),
                static request =>
                {
                    ValidatePageSize(request.PageSize);
                    ValidateHistoryLength(request.HistoryLength);
                }),
            builder.DefineStandardUnary<CancelTaskRequest, AgentTask>(
                new A2AOperationId("https://a2a-protocol.org/operations/cancel-task")),
            builder.DefineStandardStreaming<SubscribeToTaskRequest, StreamResponse>(
                new A2AOperationId("https://a2a-protocol.org/operations/subscribe-to-task")),
            builder.DefineStandardUnary<TaskPushNotificationConfig, TaskPushNotificationConfig>(
                new A2AOperationId("https://a2a-protocol.org/operations/create-task-push-notification-config")),
            builder.DefineStandardUnary<
                GetTaskPushNotificationConfigRequest,
                TaskPushNotificationConfig>(
                new A2AOperationId("https://a2a-protocol.org/operations/get-task-push-notification-config")),
            builder.DefineStandardUnary<
                ListTaskPushNotificationConfigsRequest,
                ListTaskPushNotificationConfigsResponse>(
                new A2AOperationId("https://a2a-protocol.org/operations/list-task-push-notification-configs")),
            builder.DefineStandardUnary<
                DeleteTaskPushNotificationConfigRequest,
                A2AEmptyResult>(
                new A2AOperationId("https://a2a-protocol.org/operations/delete-task-push-notification-config")),
            builder.DefineStandardUnary<GetExtendedAgentCardRequest, AgentCard>(
                new A2AOperationId("https://a2a-protocol.org/operations/get-extended-agent-card")));
    }

    private static void ValidateSendMessage(SendMessageRequest request)
    {
        if (request.Message.Parts.Count == 0)
        {
            throw new A2AException(
                "Message parts cannot be empty",
                A2AErrorCode.InvalidParams);
        }
    }

    private static void ValidateHistoryLength(int? historyLength)
    {
        if (historyLength is { } value && value < 0)
        {
            throw new A2AException(
                $"Invalid historyLength: {value}. Must be non-negative.",
                A2AErrorCode.InvalidParams);
        }
    }

    private static void ValidatePageSize(int? pageSize)
    {
        if (pageSize is { } value && (value <= 0 || value > 100))
        {
            throw new A2AException(
                $"Invalid pageSize: {value}. Must be between 1 and 100.",
                A2AErrorCode.InvalidParams);
        }
    }
}

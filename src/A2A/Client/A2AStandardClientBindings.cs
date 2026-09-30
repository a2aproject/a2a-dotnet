using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>Registers client bindings for the standard A2A operations.</summary>
public static class A2AStandardClientBindingBuilderExtensions
{
    /// <summary>Adds canonical JSON-RPC client bindings for every standard operation.</summary>
    /// <param name="builder">The client binding builder.</param>
    /// <param name="standard">The standard operation handles.</param>
    /// <returns>The builder.</returns>
    public static A2AClientOperationBindingBuilder
        AddStandardA2AJsonRpcBindings(
            this A2AClientOperationBindingBuilder builder,
            A2AStandardOperations standard)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(standard);
        builder.SetStandardOperations(standard);

        return builder
            .MapJsonRpcCore(
                standard.SendMessage,
                A2AMethods.SendMessage,
                GetTypeInfo<SendMessageRequest>(),
                GetTypeInfo<SendMessageResponse>(),
                requestCustomizer: null,
                isCanonicalStandardBinding: true)
            .MapJsonRpcStreamingCore(
                standard.SendStreamingMessage,
                A2AMethods.SendStreamingMessage,
                GetTypeInfo<SendMessageRequest>(),
                GetTypeInfo<StreamResponse>(),
                requestCustomizer: null,
                isCanonicalStandardBinding: true)
            .MapJsonRpcCore(
                standard.GetTask,
                A2AMethods.GetTask,
                GetTypeInfo<GetTaskRequest>(),
                GetTypeInfo<AgentTask>(),
                requestCustomizer: null,
                isCanonicalStandardBinding: true)
            .MapJsonRpcCore(
                standard.ListTasks,
                A2AMethods.ListTasks,
                GetTypeInfo<ListTasksRequest>(),
                GetTypeInfo<ListTasksResponse>(),
                requestCustomizer: null,
                isCanonicalStandardBinding: true)
            .MapJsonRpcCore(
                standard.CancelTask,
                A2AMethods.CancelTask,
                GetTypeInfo<CancelTaskRequest>(),
                GetTypeInfo<AgentTask>(),
                requestCustomizer: null,
                isCanonicalStandardBinding: true)
            .MapJsonRpcStreamingCore(
                standard.SubscribeToTask,
                A2AMethods.SubscribeToTask,
                GetTypeInfo<SubscribeToTaskRequest>(),
                GetTypeInfo<StreamResponse>(),
                requestCustomizer: null,
                isCanonicalStandardBinding: true)
            .MapJsonRpcCore(
                standard.CreateTaskPushNotificationConfig,
                A2AMethods.CreateTaskPushNotificationConfig,
                GetTypeInfo<TaskPushNotificationConfig>(),
                GetTypeInfo<TaskPushNotificationConfig>(),
                requestCustomizer: null,
                isCanonicalStandardBinding: true)
            .MapJsonRpcCore(
                standard.GetTaskPushNotificationConfig,
                A2AMethods.GetTaskPushNotificationConfig,
                GetTypeInfo<GetTaskPushNotificationConfigRequest>(),
                GetTypeInfo<TaskPushNotificationConfig>(),
                requestCustomizer: null,
                isCanonicalStandardBinding: true)
            .MapJsonRpcCore(
                standard.ListTaskPushNotificationConfigs,
                A2AMethods.ListTaskPushNotificationConfigs,
                GetTypeInfo<ListTaskPushNotificationConfigsRequest>(),
                GetTypeInfo<ListTaskPushNotificationConfigsResponse>(),
                requestCustomizer: null,
                isCanonicalStandardBinding: true)
            .MapJsonRpcCore(
                standard.DeleteTaskPushNotificationConfig,
                A2AMethods.DeleteTaskPushNotificationConfig,
                GetTypeInfo<DeleteTaskPushNotificationConfigRequest>(),
                GetTypeInfo<A2AEmptyResult>(),
                requestCustomizer: null,
                isCanonicalStandardBinding: true)
            .MapJsonRpcCore(
                standard.GetExtendedAgentCard,
                A2AMethods.GetExtendedAgentCard,
                GetTypeInfo<GetExtendedAgentCardRequest>(),
                GetTypeInfo<AgentCard>(),
                requestCustomizer: null,
                isCanonicalStandardBinding: true);
    }

    /// <summary>Adds canonical HTTP+JSON client bindings for every standard operation.</summary>
    /// <param name="builder">The client binding builder.</param>
    /// <param name="standard">The standard operation handles.</param>
    /// <returns>The builder.</returns>
    public static A2AClientOperationBindingBuilder AddStandardA2AHttpBindings(
        this A2AClientOperationBindingBuilder builder,
        A2AStandardOperations standard)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(standard);
        builder.SetStandardOperations(standard);

        return builder
            .MapHttpCore(
                standard.SendMessage,
                static (endpoint, request, _) => ValueTask.FromResult(
                    CreateJsonRequest(
                        HttpMethod.Post,
                        endpoint,
                        "/message:send",
                        request,
                        GetTypeInfo<SendMessageRequest>())),
                GetTypeInfo<SendMessageResponse>(),
                "SendMessage",
                isCanonicalStandardBinding: true)
            .MapHttpStreamingCore(
                standard.SendStreamingMessage,
                static (endpoint, request, _) => ValueTask.FromResult(
                    CreateJsonRequest(
                        HttpMethod.Post,
                        endpoint,
                        "/message:stream",
                        request,
                        GetTypeInfo<SendMessageRequest>())),
                GetTypeInfo<StreamResponse>(),
                "SendStreamingMessage",
                isCanonicalStandardBinding: true)
            .MapHttpCore(
                standard.GetTask,
                static (endpoint, request, _) =>
                {
                    var query = BuildQueryString(
                        (
                            "historyLength",
                            request.HistoryLength?.ToString(
                                CultureInfo.InvariantCulture)));
                    return ValueTask.FromResult(
                        CreateRequest(
                            HttpMethod.Get,
                            endpoint,
                            $"/tasks/{Uri.EscapeDataString(request.Id)}{query}"));
                },
                GetTypeInfo<AgentTask>(),
                "GetTask",
                isCanonicalStandardBinding: true)
            .MapHttpCore(
                standard.ListTasks,
                static (endpoint, request, _) =>
                {
                    var query = BuildQueryString(
                        ("contextId", request.ContextId),
                        ("status", SerializeEnumValue(request.Status)),
                        (
                            "pageSize",
                            request.PageSize?.ToString(
                                CultureInfo.InvariantCulture)),
                        ("pageToken", request.PageToken),
                        (
                            "historyLength",
                            request.HistoryLength?.ToString(
                                CultureInfo.InvariantCulture)),
                        (
                            "statusTimestampAfter",
                            request.StatusTimestampAfter?
                                .ToUniversalTime()
                                .ToString("o", CultureInfo.InvariantCulture)),
                        (
                            "includeArtifacts",
                            request.IncludeArtifacts?
                                .ToString(CultureInfo.InvariantCulture)
                                .ToLowerInvariant()));
                    return ValueTask.FromResult(
                        CreateRequest(
                            HttpMethod.Get,
                            endpoint,
                            $"/tasks{query}"));
                },
                GetTypeInfo<ListTasksResponse>(),
                "ListTasks",
                isCanonicalStandardBinding: true)
            .MapHttpCore(
                standard.CancelTask,
                static (endpoint, request, _) =>
                {
                    var message = CreateRequest(
                        HttpMethod.Post,
                        endpoint,
                        $"/tasks/{Uri.EscapeDataString(request.Id)}:cancel");
                    if (request.Metadata is not null)
                    {
                        var requestJson = JsonSerializer.SerializeToElement(
                            request,
                            GetTypeInfo<CancelTaskRequest>());
                        var metadata = requestJson.GetProperty("metadata");
                        message.Content = new StringContent(
                            $$"""{"metadata":{{metadata.GetRawText()}}}""",
                            Encoding.UTF8,
                            "application/json");
                    }

                    return ValueTask.FromResult(message);
                },
                GetTypeInfo<AgentTask>(),
                "CancelTask",
                isCanonicalStandardBinding: true)
            .MapHttpStreamingCore(
                standard.SubscribeToTask,
                static (endpoint, request, _) => ValueTask.FromResult(
                    CreateRequest(
                        HttpMethod.Post,
                        endpoint,
                        $"/tasks/{Uri.EscapeDataString(request.Id)}:subscribe")),
                GetTypeInfo<StreamResponse>(),
                "SubscribeToTask",
                isCanonicalStandardBinding: true)
            .MapHttpCore(
                standard.CreateTaskPushNotificationConfig,
                static (endpoint, request, _) =>
                {
                    ArgumentException.ThrowIfNullOrEmpty(
                        request.TaskId,
                        nameof(request.TaskId));
                    return ValueTask.FromResult(
                        CreateJsonRequest(
                            HttpMethod.Post,
                            endpoint,
                            $"/tasks/{Uri.EscapeDataString(request.TaskId)}/pushNotificationConfigs",
                            request,
                            GetTypeInfo<TaskPushNotificationConfig>()));
                },
                GetTypeInfo<TaskPushNotificationConfig>(),
                "CreateTaskPushNotificationConfig",
                isCanonicalStandardBinding: true)
            .MapHttpCore(
                standard.GetTaskPushNotificationConfig,
                static (endpoint, request, _) => ValueTask.FromResult(
                    CreateRequest(
                        HttpMethod.Get,
                        endpoint,
                        $"/tasks/{Uri.EscapeDataString(request.TaskId)}/pushNotificationConfigs/{Uri.EscapeDataString(request.Id)}")),
                GetTypeInfo<TaskPushNotificationConfig>(),
                "GetTaskPushNotificationConfig",
                isCanonicalStandardBinding: true)
            .MapHttpCore(
                standard.ListTaskPushNotificationConfigs,
                static (endpoint, request, _) =>
                {
                    var query = BuildQueryString(
                        (
                            "pageSize",
                            request.PageSize?.ToString(
                                CultureInfo.InvariantCulture)),
                        ("pageToken", request.PageToken));
                    return ValueTask.FromResult(
                        CreateRequest(
                            HttpMethod.Get,
                            endpoint,
                            $"/tasks/{Uri.EscapeDataString(request.TaskId)}/pushNotificationConfigs{query}"));
                },
                GetTypeInfo<ListTaskPushNotificationConfigsResponse>(),
                "ListTaskPushNotificationConfigs",
                isCanonicalStandardBinding: true)
            .MapHttpCore(
                standard.DeleteTaskPushNotificationConfig,
                static (endpoint, request, _) => ValueTask.FromResult(
                    CreateRequest(
                        HttpMethod.Delete,
                        endpoint,
                        $"/tasks/{Uri.EscapeDataString(request.TaskId)}/pushNotificationConfigs/{Uri.EscapeDataString(request.Id)}")),
                GetTypeInfo<A2AEmptyResult>(),
                "DeleteTaskPushNotificationConfig",
                isCanonicalStandardBinding: true)
            .MapHttpCore(
                standard.GetExtendedAgentCard,
                static (endpoint, _, _) => ValueTask.FromResult(
                    CreateRequest(
                        HttpMethod.Get,
                        endpoint,
                        "/extendedAgentCard")),
                GetTypeInfo<AgentCard>(),
                "GetExtendedAgentCard",
                isCanonicalStandardBinding: true);
    }

    private static HttpRequestMessage CreateJsonRequest<TRequest>(
        HttpMethod method,
        Uri endpoint,
        string path,
        TRequest request,
        JsonTypeInfo<TRequest> requestTypeInfo)
    {
        var message = CreateRequest(method, endpoint, path);
        message.Content = new StringContent(
            JsonSerializer.Serialize(request, requestTypeInfo),
            Encoding.UTF8,
            "application/json");
        return message;
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        Uri endpoint,
        string path) =>
        new(
            method,
            $"{endpoint.ToString().TrimEnd('/')}{path}");

    private static string BuildQueryString(
        params (string Key, string? Value)[] parameters)
    {
        var parts = new List<string>();
        foreach (var (key, value) in parameters)
        {
            if (!string.IsNullOrEmpty(value))
            {
                parts.Add($"{key}={Uri.EscapeDataString(value)}");
            }
        }

        return parts.Count == 0
            ? string.Empty
            : $"?{string.Join("&", parts)}";
    }

    private static string? SerializeEnumValue<T>(T? value)
        where T : struct, Enum =>
        value.HasValue
            ? JsonSerializer.Serialize(
                    value.Value,
                    GetTypeInfo<T>())
                .Trim('"')
            : null;

    private static JsonTypeInfo<T> GetTypeInfo<T>() =>
        (JsonTypeInfo<T>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));
}

internal sealed class A2AStandardClientBindings
{
    private static readonly Lazy<A2AStandardClientBindings> s_default =
        new(Create);

    private A2AStandardClientBindings(
        A2AStandardOperations operations,
        A2AClientOperationBindings bindings)
    {
        Operations = operations;
        Bindings = bindings;
    }

    internal static A2AStandardClientBindings Default => s_default.Value;

    internal A2AStandardOperations Operations { get; }

    internal A2AClientOperationBindings Bindings { get; }

    internal static A2AClientOperationBindings IncludeDefaults(
        A2AClientOperationBindings bindings) =>
        bindings.StandardOperations is null
            ? A2AClientOperationBindings.Combine(Default.Bindings, bindings)
            : bindings;

    private static A2AStandardClientBindings Create()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operations = operationBuilder.AddStandardA2AOperations();
        var catalog = operationBuilder.Build();
        var bindings = new A2AClientOperationBindingBuilder()
            .AddStandardA2AJsonRpcBindings(operations)
            .AddStandardA2AHttpBindings(operations)
            .Build(catalog);
        return new A2AStandardClientBindings(operations, bindings);
    }
}

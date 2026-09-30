using System.Text.Json.Serialization.Metadata;

namespace A2A.AspNetCore;

/// <summary>Registers JSON-RPC bindings for the standard A2A operations.</summary>
public static class A2AStandardJsonRpcBindingBuilderExtensions
{
    /// <summary>Adds JSON-RPC bindings for every standard A2A operation.</summary>
    /// <param name="builder">The JSON-RPC binding builder.</param>
    /// <param name="standard">The standard operation handles.</param>
    /// <returns>The builder.</returns>
    public static A2AJsonRpcOperationBindingBuilder AddStandardA2AJsonRpcBindings(
        this A2AJsonRpcOperationBindingBuilder builder,
        A2AStandardOperations standard)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(standard);

        return builder
            .Map(
                A2AMethods.SendMessage,
                standard.SendMessage,
                GetTypeInfo<SendMessageRequest>(),
                GetTypeInfo<SendMessageResponse>())
            .MapStreaming(
                A2AMethods.SendStreamingMessage,
                standard.SendStreamingMessage,
                GetTypeInfo<SendMessageRequest>(),
                GetTypeInfo<StreamResponse>())
            .Map(
                A2AMethods.GetTask,
                standard.GetTask,
                GetTypeInfo<GetTaskRequest>(),
                GetTypeInfo<AgentTask>())
            .Map(
                A2AMethods.ListTasks,
                standard.ListTasks,
                GetTypeInfo<ListTasksRequest>(),
                GetTypeInfo<ListTasksResponse>())
            .Map(
                A2AMethods.CancelTask,
                standard.CancelTask,
                GetTypeInfo<CancelTaskRequest>(),
                GetTypeInfo<AgentTask>())
            .MapStreaming(
                A2AMethods.SubscribeToTask,
                standard.SubscribeToTask,
                GetTypeInfo<SubscribeToTaskRequest>(),
                GetTypeInfo<StreamResponse>())
            .MapCore(
                A2AMethods.CreateTaskPushNotificationConfig,
                standard.CreateTaskPushNotificationConfig,
                GetTypeInfo<TaskPushNotificationConfig>(),
                GetTypeInfo<TaskPushNotificationConfig>(),
                ProbePushNotificationSupportAsync)
            .MapCore(
                A2AMethods.GetTaskPushNotificationConfig,
                standard.GetTaskPushNotificationConfig,
                GetTypeInfo<GetTaskPushNotificationConfigRequest>(),
                GetTypeInfo<TaskPushNotificationConfig>(),
                ProbePushNotificationSupportAsync)
            .MapCore(
                A2AMethods.ListTaskPushNotificationConfigs,
                standard.ListTaskPushNotificationConfigs,
                GetTypeInfo<ListTaskPushNotificationConfigsRequest>(),
                GetTypeInfo<ListTaskPushNotificationConfigsResponse>(),
                ProbePushNotificationSupportAsync)
            .MapCore(
                A2AMethods.DeleteTaskPushNotificationConfig,
                standard.DeleteTaskPushNotificationConfig,
                GetTypeInfo<DeleteTaskPushNotificationConfigRequest>(),
                GetTypeInfo<A2AEmptyResult>(),
                ProbePushNotificationSupportAsync)
            .Map(
                A2AMethods.GetExtendedAgentCard,
                standard.GetExtendedAgentCard,
                GetTypeInfo<GetExtendedAgentCardRequest>(),
                GetTypeInfo<AgentCard>());
    }

    private static JsonTypeInfo<T> GetTypeInfo<T>() =>
        (JsonTypeInfo<T>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

    private static async ValueTask ProbePushNotificationSupportAsync(
        A2AOperationContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.RequestHandler.GetTaskPushNotificationConfigAsync(
                null!,
                cancellationToken).ConfigureAwait(false);
        }
        catch (A2AException ex)
            when (ex.ErrorCode == A2AErrorCode.PushNotificationNotSupported)
        {
            throw;
        }
        catch (Exception)
        {
            // The legacy probe treats any non-not-supported outcome as support.
            return;
        }
    }
}

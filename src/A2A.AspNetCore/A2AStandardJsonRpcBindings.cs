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

    internal static void ValidateReservedMethodBinding(
        string method,
        A2AOperationRegistration registration)
    {
        var expectedOperationId = method switch
        {
            A2AMethods.SendMessage =>
                "https://a2a-protocol.org/operations/send-message",
            A2AMethods.SendStreamingMessage =>
                "https://a2a-protocol.org/operations/send-message-stream",
            A2AMethods.GetTask =>
                "https://a2a-protocol.org/operations/get-task",
            A2AMethods.ListTasks =>
                "https://a2a-protocol.org/operations/list-tasks",
            A2AMethods.CancelTask =>
                "https://a2a-protocol.org/operations/cancel-task",
            A2AMethods.SubscribeToTask =>
                "https://a2a-protocol.org/operations/subscribe-to-task",
            A2AMethods.CreateTaskPushNotificationConfig =>
                "https://a2a-protocol.org/operations/create-task-push-notification-config",
            A2AMethods.GetTaskPushNotificationConfig =>
                "https://a2a-protocol.org/operations/get-task-push-notification-config",
            A2AMethods.ListTaskPushNotificationConfigs =>
                "https://a2a-protocol.org/operations/list-task-push-notification-configs",
            A2AMethods.DeleteTaskPushNotificationConfig =>
                "https://a2a-protocol.org/operations/delete-task-push-notification-config",
            A2AMethods.GetExtendedAgentCard =>
                "https://a2a-protocol.org/operations/get-extended-agent-card",
            _ => null,
        };
        if (expectedOperationId is null)
        {
            return;
        }

        if (registration.Source != A2AOperationSource.Standard
            || !string.Equals(
                registration.Id.Value,
                expectedOperationId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The reserved JSON-RPC method '{method}' must map to the standard A2A operation '{expectedOperationId}'.");
        }
    }

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

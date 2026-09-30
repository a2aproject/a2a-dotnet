namespace A2A;

/// <summary>Registers standard operation handlers that delegate to <see cref="IA2ARequestHandler"/>.</summary>
public static class A2AStandardOperationHandlerCatalogBuilderExtensions
{
    /// <summary>Adds handlers for every standard A2A operation.</summary>
    /// <param name="builder">The handler catalog builder.</param>
    /// <param name="standard">The standard operation handles.</param>
    /// <returns>The builder.</returns>
    public static A2AOperationHandlerCatalogBuilder AddStandardA2AHandlers(
        this A2AOperationHandlerCatalogBuilder builder,
        A2AStandardOperations standard)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(standard);

        return builder
            .Map(
                standard.SendMessage,
                static (context, request, cancellationToken) =>
                    new(context.RequestHandler.SendMessageAsync(request, cancellationToken)))
            .MapStreaming(
                standard.SendStreamingMessage,
                static (context, request, cancellationToken) =>
                    context.RequestHandler.SendStreamingMessageAsync(
                        request,
                        cancellationToken))
            .Map(
                standard.GetTask,
                static (context, request, cancellationToken) =>
                    new(context.RequestHandler.GetTaskAsync(request, cancellationToken)))
            .Map(
                standard.ListTasks,
                static (context, request, cancellationToken) =>
                    new(context.RequestHandler.ListTasksAsync(request, cancellationToken)))
            .Map(
                standard.CancelTask,
                static (context, request, cancellationToken) =>
                    new(context.RequestHandler.CancelTaskAsync(request, cancellationToken)))
            .MapStreaming(
                standard.SubscribeToTask,
                static (context, request, cancellationToken) =>
                    context.RequestHandler.SubscribeToTaskAsync(
                        request,
                        cancellationToken))
            .Map(
                standard.CreateTaskPushNotificationConfig,
                static (context, request, cancellationToken) =>
                    new(context.RequestHandler.CreateTaskPushNotificationConfigAsync(
                        request,
                        cancellationToken)))
            .Map(
                standard.GetTaskPushNotificationConfig,
                static (context, request, cancellationToken) =>
                    new(context.RequestHandler.GetTaskPushNotificationConfigAsync(
                        request,
                        cancellationToken)))
            .Map(
                standard.ListTaskPushNotificationConfigs,
                static (context, request, cancellationToken) =>
                    new(context.RequestHandler.ListTaskPushNotificationConfigsAsync(
                        request,
                        cancellationToken)))
            .Map(
                standard.DeleteTaskPushNotificationConfig,
                DeleteTaskPushNotificationConfigAsync)
            .Map(
                standard.GetExtendedAgentCard,
                static (context, request, cancellationToken) =>
                    new(context.RequestHandler.GetExtendedAgentCardAsync(
                        request,
                        cancellationToken)));
    }

    private static async ValueTask<A2AEmptyResult> DeleteTaskPushNotificationConfigAsync(
        A2AOperationContext context,
        DeleteTaskPushNotificationConfigRequest request,
        CancellationToken cancellationToken)
    {
        await context.RequestHandler.DeleteTaskPushNotificationConfigAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        return A2AEmptyResult.Instance;
    }
}

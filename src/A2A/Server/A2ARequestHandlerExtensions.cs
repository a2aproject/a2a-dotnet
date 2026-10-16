namespace A2A;

/// <summary>Adapts standard handlers to an explicitly negotiated request snapshot.</summary>
public static class A2ARequestHandlerExtensions
{
    /// <summary>
    /// Binds an immutable snapshot to one request without modifying the shared handler.
    /// </summary>
    /// <param name="handler">The standard request handler.</param>
    /// <param name="requestContext">The negotiated snapshot, or null for an unnegotiated request.</param>
    /// <returns>A request-bound adapter, or the original handler when it does not opt in.</returns>
    /// <remarks>
    /// Context-aware handlers implement IA2ARequestHandlerWithContext. The adapter captures
    /// only the immutable SDK context, not HttpContext, ServerCallContext or scoped services.
    /// Streaming enumeration and detached agent execution can therefore retain the snapshot
    /// without depending on the transport lifetime. Rebinding replaces the prior snapshot.
    /// </remarks>
    public static IA2ARequestHandler WithRequestContext(this IA2ARequestHandler handler, A2ARequestContext? requestContext)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (handler is ContextualHandler bound) handler = bound.Handler;
        return requestContext is not null && handler is IA2ARequestHandlerWithContext contextual
            ? new ContextualHandler(contextual, requestContext)
            : handler;
    }

    private sealed class ContextualHandler(IA2ARequestHandlerWithContext handler, A2ARequestContext context) : IA2ARequestHandler
    {
        internal IA2ARequestHandlerWithContext Handler => handler;

        public Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
            => handler.SendMessageAsync(request, context, cancellationToken);

        public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
            => handler.SendStreamingMessageAsync(request, context, cancellationToken);

        public Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken = default)
            => handler.GetTaskAsync(request, context, cancellationToken);

        public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
            => handler.ListTasksAsync(request, context, cancellationToken);

        public Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken = default)
            => handler.CancelTaskAsync(request, context, cancellationToken);

        public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken cancellationToken = default)
            => handler.SubscribeToTaskAsync(request, context, cancellationToken);

        public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(TaskPushNotificationConfig config, CancellationToken cancellationToken = default)
            => handler.CreateTaskPushNotificationConfigAsync(config, context, cancellationToken);

        public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
            => handler.GetTaskPushNotificationConfigAsync(request, context, cancellationToken);

        public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken cancellationToken = default)
            => handler.ListTaskPushNotificationConfigsAsync(request, context, cancellationToken);

        public Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
            => handler.DeleteTaskPushNotificationConfigAsync(request, context, cancellationToken);

        public Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default)
            => handler.GetExtendedAgentCardAsync(request, context, cancellationToken);
    }
}

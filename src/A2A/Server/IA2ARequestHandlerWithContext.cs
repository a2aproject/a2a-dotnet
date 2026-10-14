namespace A2A;

/// <summary>
/// Opts a standard request handler into explicit, transport-neutral request context propagation.
/// </summary>
/// <remarks>
/// Existing IA2ARequestHandler implementations remain valid. Override the context-aware
/// operations that need activation; the default implementations delegate to the legacy
/// operations. All three arguments are required to preserve existing two-argument calls.
/// A null context means the request did not use registry-aware negotiation.
/// </remarks>
public interface IA2ARequestHandlerWithContext : IA2ARequestHandler
{
    /// <summary>Handles a send message request with its negotiated activation.</summary>
    /// <param name="request">The request.</param>
    /// <param name="requestContext">The immutable negotiated snapshot, or null.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The response.</returns>
    Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken)
        => SendMessageAsync(request, cancellationToken);

    /// <summary>Handles a streaming send with its negotiated activation.</summary>
    /// <param name="request">The request.</param>
    /// <param name="requestContext">The immutable negotiated snapshot, or null.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The response events.</returns>
    IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken)
        => SendStreamingMessageAsync(request, cancellationToken);

    /// <summary>Gets a task with the negotiated request context.</summary>
    /// <param name="request">The request.</param>
    /// <param name="requestContext">The immutable negotiated snapshot, or null.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The task.</returns>
    Task<AgentTask> GetTaskAsync(GetTaskRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken)
        => GetTaskAsync(request, cancellationToken);

    /// <summary>Lists tasks with the negotiated request context.</summary>
    /// <param name="request">The request.</param>
    /// <param name="requestContext">The immutable negotiated snapshot, or null.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The task list.</returns>
    Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken)
        => ListTasksAsync(request, cancellationToken);

    /// <summary>Cancels a task with the negotiated request context.</summary>
    /// <param name="request">The request.</param>
    /// <param name="requestContext">The immutable negotiated snapshot, or null.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The canceled task.</returns>
    Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken)
        => CancelTaskAsync(request, cancellationToken);

    /// <summary>Subscribes to a task with the negotiated request context.</summary>
    /// <param name="request">The request.</param>
    /// <param name="requestContext">The immutable negotiated snapshot, or null.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The response events.</returns>
    IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken)
        => SubscribeToTaskAsync(request, cancellationToken);

    /// <summary>Creates a push configuration with the negotiated request context.</summary>
    /// <param name="config">The configuration.</param>
    /// <param name="requestContext">The immutable negotiated snapshot, or null.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The configuration.</returns>
    Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(TaskPushNotificationConfig config, A2ARequestContext? requestContext, CancellationToken cancellationToken)
        => CreateTaskPushNotificationConfigAsync(config, cancellationToken);

    /// <summary>Gets a push configuration with the negotiated request context.</summary>
    /// <param name="request">The request.</param>
    /// <param name="requestContext">The immutable negotiated snapshot, or null.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The configuration.</returns>
    Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken)
        => GetTaskPushNotificationConfigAsync(request, cancellationToken);

    /// <summary>Lists push configurations with the negotiated request context.</summary>
    /// <param name="request">The request.</param>
    /// <param name="requestContext">The immutable negotiated snapshot, or null.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The configuration list.</returns>
    Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(ListTaskPushNotificationConfigsRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken)
        => ListTaskPushNotificationConfigsAsync(request, cancellationToken);

    /// <summary>Deletes a push configuration with the negotiated request context.</summary>
    /// <param name="request">The request.</param>
    /// <param name="requestContext">The immutable negotiated snapshot, or null.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The deletion operation.</returns>
    Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken)
        => DeleteTaskPushNotificationConfigAsync(request, cancellationToken);

    /// <summary>Gets an extended card with the negotiated request context.</summary>
    /// <param name="request">The request.</param>
    /// <param name="requestContext">The immutable negotiated snapshot, or null.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The card.</returns>
    Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken)
        => GetExtendedAgentCardAsync(request, cancellationToken);
}

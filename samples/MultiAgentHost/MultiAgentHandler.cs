using A2A;

namespace MultiAgentHost;

/// <summary>
/// Adapter <see cref="IA2ARequestHandler"/> that delegates every call to the
/// subdomain-specific <see cref="A2AServer"/> resolved via <see cref="A2AServerFactory"/>.
/// </summary>
/// <remarks>
/// The subdomain is extracted by <see cref="SubdomainMiddleware"/> and stored in the
/// scoped <see cref="SubdomainContext"/>. This handler reads it via
/// <see cref="IHttpContextAccessor"/> and resolves the correct <see cref="A2AServer"/>
/// from the factory.
/// <para>
/// Each <see cref="A2AServer"/> has its own <see cref="InMemoryTaskStore"/> and
/// <see cref="ChannelEventNotifier"/>, guaranteeing complete task isolation between agents.
/// </para>
/// </remarks>
public sealed class MultiAgentHandler(
    A2AServerFactory factory,
    IHttpContextAccessor httpContextAccessor) : IA2ARequestHandler
{
    private A2AServer Resolve()
    {
        var ctx = httpContextAccessor.HttpContext
            ?? throw new A2AException("No active HTTP request.", A2AErrorCode.InvalidRequest);

        var subdomainCtx = ctx.RequestServices.GetRequiredService<SubdomainContext>();

        if (string.IsNullOrEmpty(subdomainCtx.Subdomain))
            throw new A2AException("Agent subdomain not resolved. Use a subdomain or set the X-Agent-Subdomain header.", A2AErrorCode.InvalidRequest);

        return factory.GetServer(subdomainCtx.Subdomain)
            ?? throw new A2AException($"Unknown agent subdomain: '{subdomainCtx.Subdomain}'.", A2AErrorCode.InvalidRequest);
    }

    public Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken)
        => Resolve().SendMessageAsync(request, cancellationToken);

    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken cancellationToken)
        => Resolve().SendStreamingMessageAsync(request, cancellationToken);

    public Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken)
        => Resolve().GetTaskAsync(request, cancellationToken);

    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken)
        => Resolve().ListTasksAsync(request, cancellationToken);

    public Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken)
        => Resolve().CancelTaskAsync(request, cancellationToken);

    public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken cancellationToken)
        => Resolve().SubscribeToTaskAsync(request, cancellationToken);

    public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(CreateTaskPushNotificationConfigRequest request, CancellationToken cancellationToken)
        => Resolve().CreateTaskPushNotificationConfigAsync(request, cancellationToken);

    public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken)
        => Resolve().GetTaskPushNotificationConfigAsync(request, cancellationToken);

    public Task<ListTaskPushNotificationConfigResponse> ListTaskPushNotificationConfigAsync(ListTaskPushNotificationConfigRequest request, CancellationToken cancellationToken)
        => Resolve().ListTaskPushNotificationConfigAsync(request, cancellationToken);

    public Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken)
        => Resolve().DeleteTaskPushNotificationConfigAsync(request, cancellationToken);

    public Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken)
        => Resolve().GetExtendedAgentCardAsync(request, cancellationToken);
}

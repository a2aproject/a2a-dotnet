namespace A2A.Grpc.UnitTests;

using System.Runtime.CompilerServices;
using A2A;

/// <summary>
/// A configurable <see cref="IA2ARequestHandler"/> test double. It records the last request for each
/// operation, returns canned responses, and — when <see cref="Error"/> is set — throws it so error
/// propagation across the gRPC boundary can be verified.
/// </summary>
internal sealed class FakeRequestHandler : IA2ARequestHandler
{
    public A2AException? Error { get; set; }

    public A2ARequestContext? LastRequestContext { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public SendMessageResponse SendMessageResult { get; set; } = new();

    public AgentTask TaskResult { get; set; } = new() { Id = "task", ContextId = "ctx" };

    public ListTasksResponse ListTasksResult { get; set; } = new();

    public TaskPushNotificationConfig PushConfigResult { get; set; } = new() { Id = "cfg", TaskId = "task" };

    public ListTaskPushNotificationConfigsResponse ListPushConfigResult { get; set; } = new();

    public AgentCard AgentCardResult { get; set; } = new() { Name = "Agent", Description = "d", Version = "1.0.0" };

    public List<StreamResponse> StreamEvents { get; } = [];

    public SendMessageRequest? LastSendMessage { get; private set; }

    public GetTaskRequest? LastGetTask { get; private set; }

    public TaskPushNotificationConfig? LastCreateConfig { get; private set; }

    public DeleteTaskPushNotificationConfigRequest? LastDeleteConfig { get; private set; }

    public Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default)
    {
        LastSendMessage = request;
        ThrowIfConfigured(requestContext, cancellationToken);
        return Task.FromResult(SendMessageResult);
    }

    public Task<AgentTask> GetTaskAsync(GetTaskRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default)
    {
        LastGetTask = request;
        ThrowIfConfigured(requestContext, cancellationToken);
        return Task.FromResult(TaskResult);
    }

    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured(requestContext, cancellationToken);
        return Task.FromResult(ListTasksResult);
    }

    public Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured(requestContext, cancellationToken);
        return Task.FromResult(TaskResult);
    }

    public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(TaskPushNotificationConfig config, A2ARequestContext? requestContext, CancellationToken cancellationToken = default)
    {
        LastCreateConfig = config;
        ThrowIfConfigured(requestContext, cancellationToken);
        return Task.FromResult(PushConfigResult);
    }

    public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured(requestContext, cancellationToken);
        return Task.FromResult(PushConfigResult);
    }

    public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(ListTaskPushNotificationConfigsRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured(requestContext, cancellationToken);
        return Task.FromResult(ListPushConfigResult);
    }

    public Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default)
    {
        LastDeleteConfig = request;
        ThrowIfConfigured(requestContext, cancellationToken);
        return Task.CompletedTask;
    }

    public Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, A2ARequestContext? requestContext, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured(requestContext, cancellationToken);
        return Task.FromResult(AgentCardResult);
    }

    public async IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, A2ARequestContext? requestContext, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastSendMessage = request;
        ThrowIfConfigured(requestContext, cancellationToken);
        foreach (var streamEvent in StreamEvents)
        {
            await Task.Yield();
            yield return streamEvent;
        }
    }

    public async IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, A2ARequestContext? requestContext, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured(requestContext, cancellationToken);
        foreach (var streamEvent in StreamEvents)
        {
            await Task.Yield();
            yield return streamEvent;
        }
    }

    private void ThrowIfConfigured(A2ARequestContext? requestContext, CancellationToken cancellationToken)
    {
        LastRequestContext = requestContext;
        LastCancellationToken = cancellationToken;
        if (Error is not null)
        {
            throw Error;
        }
    }
}

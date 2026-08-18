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

    public SendMessageResponse SendMessageResult { get; set; } = new();

    public AgentTask TaskResult { get; set; } = new() { Id = "task", ContextId = "ctx" };

    public ListTasksResponse ListTasksResult { get; set; } = new();

    public TaskPushNotificationConfig PushConfigResult { get; set; } = new() { Id = "cfg", TaskId = "task" };

    public ListTaskPushNotificationConfigResponse ListPushConfigResult { get; set; } = new();

    public AgentCard AgentCardResult { get; set; } = new() { Name = "Agent", Description = "d", Version = "1.0.0" };

    public List<StreamResponse> StreamEvents { get; } = [];

    public SendMessageRequest? LastSendMessage { get; private set; }

    public GetTaskRequest? LastGetTask { get; private set; }

    public CreateTaskPushNotificationConfigRequest? LastCreateConfig { get; private set; }

    public DeleteTaskPushNotificationConfigRequest? LastDeleteConfig { get; private set; }

    public Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        LastSendMessage = request;
        ThrowIfConfigured();
        return Task.FromResult(SendMessageResult);
    }

    public Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken = default)
    {
        LastGetTask = request;
        ThrowIfConfigured();
        return Task.FromResult(TaskResult);
    }

    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured();
        return Task.FromResult(ListTasksResult);
    }

    public Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured();
        return Task.FromResult(TaskResult);
    }

    public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(CreateTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        LastCreateConfig = request;
        ThrowIfConfigured();
        return Task.FromResult(PushConfigResult);
    }

    public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured();
        return Task.FromResult(PushConfigResult);
    }

    public Task<ListTaskPushNotificationConfigResponse> ListTaskPushNotificationConfigAsync(ListTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured();
        return Task.FromResult(ListPushConfigResult);
    }

    public Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        LastDeleteConfig = request;
        ThrowIfConfigured();
        return Task.CompletedTask;
    }

    public Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured();
        return Task.FromResult(AgentCardResult);
    }

    public async IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastSendMessage = request;
        ThrowIfConfigured();
        foreach (var streamEvent in StreamEvents)
        {
            await Task.Yield();
            yield return streamEvent;
        }
    }

    public async IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured();
        foreach (var streamEvent in StreamEvents)
        {
            await Task.Yield();
            yield return streamEvent;
        }
    }

    private void ThrowIfConfigured()
    {
        if (Error is not null)
        {
            throw Error;
        }
    }
}

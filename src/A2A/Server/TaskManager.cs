using A2A.Extensions;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace A2A;

/// <summary>Default implementation of <see cref="ITaskManager"/> that delegates to callbacks.</summary>
public sealed class TaskManager : ITaskManager
{
    private readonly ITaskStore _store;
    private readonly ILogger<TaskManager> _logger;

    /// <summary>Gets or sets the callback invoked when a message is sent.</summary>
    public Func<SendMessageRequest, CancellationToken, Task<SendMessageResponse>>? OnSendMessage { get; set; }

    /// <summary>Gets or sets the callback invoked when a streaming message is sent.</summary>
    public Func<SendMessageRequest, CancellationToken, IAsyncEnumerable<StreamResponse>>? OnSendStreamingMessage { get; set; }

    /// <summary>Gets or sets the callback invoked when a task is canceled.</summary>
    public Func<CancelTaskRequest, CancellationToken, Task<AgentTask>>? OnCancelTask { get; set; }

    /// <summary>Initializes a new instance of the <see cref="TaskManager"/> class.</summary>
    /// <param name="store">The task store used for persistence.</param>
    /// <param name="logger">The logger instance.</param>
    public TaskManager(ITaskStore store, ILogger<TaskManager> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        if (OnSendMessage is null)
        {
            throw new A2AException("SendMessage not supported.", A2AErrorCode.UnsupportedOperation);
        }

        return await OnSendMessage(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        if (OnSendStreamingMessage is null)
        {
            throw new A2AException("SendStreamingMessage not supported.", A2AErrorCode.UnsupportedOperation);
        }

        return OnSendStreamingMessage(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken = default)
    {
        var task = await _store.GetTaskAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new A2AException($"Task '{request.Id}' not found.", A2AErrorCode.TaskNotFound);

        return task.WithHistoryTrimmedTo(request.HistoryLength);
    }

    /// <inheritdoc />
    public async Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        return await _store.ListTasksAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken = default)
    {
        if (OnCancelTask is null)
        {
            throw new A2AException("CancelTask not supported.", A2AErrorCode.TaskNotCancelable);
        }

        return await OnCancelTask(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = await _store.GetTaskAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new A2AException($"Task '{request.Id}' not found.", A2AErrorCode.TaskNotFound);

        // Default implementation: yield nothing (override for real implementations)
        yield break;
    }

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(CreateTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        throw new A2AException("Push notifications not supported.", A2AErrorCode.PushNotificationNotSupported);
    }

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        throw new A2AException("Push notifications not supported.", A2AErrorCode.PushNotificationNotSupported);
    }

    /// <inheritdoc />
    public Task<ListTaskPushNotificationConfigResponse> ListTaskPushNotificationConfigAsync(ListTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        throw new A2AException("Push notifications not supported.", A2AErrorCode.PushNotificationNotSupported);
    }

    /// <inheritdoc />
    public Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        throw new A2AException("Push notifications not supported.", A2AErrorCode.PushNotificationNotSupported);
    }

    /// <inheritdoc />
    public Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default)
    {
        throw new A2AException("Extended agent card not configured.", A2AErrorCode.ExtendedAgentCardNotConfigured);
    }
}

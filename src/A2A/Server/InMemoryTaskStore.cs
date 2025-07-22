namespace A2A;

/// <summary>
/// In-memory implementation of task store for development and testing.
/// </summary>
public sealed class InMemoryTaskStore : ITaskStore
{
    private readonly Dictionary<string, AgentTask> _taskCache = [];
    private readonly Dictionary<string, List<TaskPushNotificationConfig>> _pushNotificationCache = [];

    /// <inheritdoc />
    public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AgentTask?>(cancellationToken);
        }

        return string.IsNullOrEmpty(taskId)
            ? Task.FromException<AgentTask?>(new ArgumentNullException(nameof(taskId)))
            : Task.FromResult(_taskCache.TryGetValue(taskId, out var task) ? task : null);
    }

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig?> GetPushNotificationAsync(string taskId, string notificationConfigId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<TaskPushNotificationConfig?>(cancellationToken);
        }

        if (string.IsNullOrEmpty(taskId))
        {
            return Task.FromException<TaskPushNotificationConfig?>(new ArgumentNullException(nameof(taskId)));
        }

        if (!_pushNotificationCache.TryGetValue(taskId, out var pushNotificationConfigs))
        {
            return Task.FromResult<TaskPushNotificationConfig?>(null);
        }

        var pushNotificationConfig = pushNotificationConfigs.FirstOrDefault(config => config.PushNotificationConfig.Id == notificationConfigId);

        return Task.FromResult<TaskPushNotificationConfig?>(pushNotificationConfig);
    }

    /// <inheritdoc />
    public Task<AgentTaskStatus> UpdateStatusAsync(string taskId, TaskState status, Message? message = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AgentTaskStatus>(cancellationToken);
        }

        if (string.IsNullOrEmpty(taskId))
        {
            return Task.FromException<AgentTaskStatus>(new ArgumentNullException(nameof(taskId)));
        }

        if (!_taskCache.TryGetValue(taskId, out var task))
        {
            throw new ArgumentException("Task not found.");
        }

        task.Status.State = status;
        task.Status.Message = message;
        task.Status.Timestamp = DateTime.UtcNow;
        return Task.FromResult(task.Status);
    }

    /// <inheritdoc />
    public Task SetTaskAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        _taskCache[task.Id] = task;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetPushNotificationConfigAsync(TaskPushNotificationConfig pushNotificationConfig, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (pushNotificationConfig is null)
        {
            return Task.FromException(new ArgumentNullException(nameof(pushNotificationConfig)));
        }

        if (!_pushNotificationCache.TryGetValue(pushNotificationConfig.TaskId, out var pushNotificationConfigs))
        {
            pushNotificationConfigs = [];
            _pushNotificationCache[pushNotificationConfig.TaskId] = pushNotificationConfigs;
        }

        pushNotificationConfigs.Add(pushNotificationConfig);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IEnumerable<TaskPushNotificationConfig>> GetPushNotificationsAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IEnumerable<TaskPushNotificationConfig>>(cancellationToken);
        }

        if (!_pushNotificationCache.TryGetValue(taskId, out var pushNotificationConfigs))
        {
            return Task.FromResult<IEnumerable<TaskPushNotificationConfig>>([]);
        }

        return Task.FromResult<IEnumerable<TaskPushNotificationConfig>>(pushNotificationConfigs);
    }
}
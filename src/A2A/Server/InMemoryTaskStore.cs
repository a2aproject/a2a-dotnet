namespace A2A;

/// <summary>
/// In-memory implementation of task store for development and testing.
/// </summary>
public sealed class InMemoryTaskStore : ITaskStore
{
    private readonly Dictionary<string, AgentTask> _taskCache = [];
    private readonly Dictionary<string, TaskPushNotificationConfig> _pushNotificationCache = [];

    /// <inheritdoc />
    public Task<AgentTask?> GetTaskAsync(string taskId) =>
        string.IsNullOrEmpty(taskId)
            ? Task.FromException<AgentTask?>(new ArgumentNullException(taskId))
            : Task.FromResult(_taskCache.TryGetValue(taskId, out var task) ? task : null);

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig?> GetPushNotificationAsync(string taskId)
    {
        if (string.IsNullOrEmpty(taskId))
        {
            return Task.FromException<TaskPushNotificationConfig?>(new ArgumentNullException(taskId));
        }

        _pushNotificationCache.TryGetValue(taskId, out var pushNotificationConfig);
        return Task.FromResult<TaskPushNotificationConfig?>(pushNotificationConfig);
    }

    /// <inheritdoc />
    public Task<AgentTaskStatus> UpdateStatusAsync(string taskId, TaskState status, Message? message = null)
    {
        if (string.IsNullOrEmpty(taskId))
        {
            return Task.FromException<AgentTaskStatus>(new ArgumentNullException(taskId));
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
    public Task SetTaskAsync(AgentTask task)
    {
        _taskCache[task.Id] = task;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetPushNotificationConfigAsync(TaskPushNotificationConfig pushNotificationConfig)
    {
        if (pushNotificationConfig is null)
        {
            return Task.FromException(new ArgumentNullException(nameof(pushNotificationConfig)));
        }

        _pushNotificationCache[pushNotificationConfig.TaskId] = pushNotificationConfig;
        return Task.CompletedTask;
    }
}
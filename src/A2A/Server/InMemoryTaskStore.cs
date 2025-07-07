namespace A2A;

/// <summary>
/// In-memory implementation of task store for development and testing.
/// </summary>
public class InMemoryTaskStore : ITaskStore
{
    private readonly Dictionary<string, AgentTask> _taskCache = [];
    private readonly Dictionary<string, TaskPushNotificationConfig> _pushNotificationCache = [];

    /// <inheritdoc />
    public Task<AgentTask?> GetTaskAsync(string taskId) =>
        Task.FromResult(
            string.IsNullOrEmpty(taskId) ? null :
            _taskCache.TryGetValue(taskId, out var task) ? task :
            null);

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig?> GetPushNotificationAsync(string taskId)
    {
        _pushNotificationCache.TryGetValue(taskId, out var pushNotificationConfig);
        return Task.FromResult<TaskPushNotificationConfig?>(pushNotificationConfig);
    }

    /// <inheritdoc />
    public Task<AgentTaskStatus> UpdateStatusAsync(string taskId, TaskState status, Message? message = null)
    {
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
        _pushNotificationCache[pushNotificationConfig.Id] = pushNotificationConfig;
        return Task.CompletedTask;
    }
}
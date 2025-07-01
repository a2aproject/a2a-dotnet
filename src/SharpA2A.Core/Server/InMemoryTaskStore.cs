namespace A2A.Core;

public class InMemoryTaskStore : ITaskStore
{
    private Dictionary<string, AgentTask> _TaskCache { get; set; } = new Dictionary<string, AgentTask>();
    private Dictionary<string, TaskPushNotificationConfig> _PushNotificationCache { get; set; } = new Dictionary<string, TaskPushNotificationConfig>();


    public Task<AgentTask?> GetTaskAsync(string taskId)
    {
        if (string.IsNullOrEmpty(taskId))
        {
            return Task.FromResult<AgentTask?>(null);
        }

        if (_TaskCache.TryGetValue(taskId, out var task))
        {
            return Task.FromResult<AgentTask?>(task);
        }
        return Task.FromResult<AgentTask?>(null);
    }

    public Task<TaskPushNotificationConfig?> GetPushNotificationAsync(string taskId)
    {
        if (_PushNotificationCache.TryGetValue(taskId, out var pushNotificationConfig))
        {
            return Task.FromResult<TaskPushNotificationConfig?>(pushNotificationConfig);
        }
        return Task.FromResult<TaskPushNotificationConfig?>(null);
    }

    public Task<AgentTaskStatus> UpdateStatusAsync(string taskId, TaskState status, Message? message = null)
    {
        if (_TaskCache.TryGetValue(taskId, out var task))
        {
            task.Status.State = status;
            task.Status.Message = message;
            task.Status.Timestamp = DateTime.UtcNow;
            return Task.FromResult(task.Status);
        }
        else
        {
            throw new ArgumentException("Task not found.");
        }
    }

    public Task SetTaskAsync(AgentTask task)
    {
        if (_TaskCache.ContainsKey(task.Id))
        {
            _TaskCache[task.Id] = task;
        }
        else
        {
            _TaskCache.Add(task.Id, task);
        }
        return Task.CompletedTask;
    }

    public Task SetPushNotificationConfigAsync(TaskPushNotificationConfig pushNotificationConfig)
    {
        if (_PushNotificationCache.ContainsKey(pushNotificationConfig.Id))
        {
            _PushNotificationCache[pushNotificationConfig.Id] = pushNotificationConfig;
        }
        else
        {
            _PushNotificationCache.Add(pushNotificationConfig.Id, pushNotificationConfig);
        }
        return Task.CompletedTask;
    }
}


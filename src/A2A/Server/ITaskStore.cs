namespace A2A;

public interface ITaskStore
{
    Task<AgentTask?> GetTaskAsync(string taskId);
    Task<TaskPushNotificationConfig?> GetPushNotificationAsync(string taskId);
    Task<AgentTaskStatus> UpdateStatusAsync(string taskId, TaskState status, Message? message = null);
    Task SetTaskAsync(AgentTask task);
    Task SetPushNotificationConfigAsync(TaskPushNotificationConfig pushNotificationConfig);
}
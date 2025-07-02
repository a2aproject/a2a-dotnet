using System.Net.ServerSentEvents;

namespace A2A;

public interface IA2AClient
{
    Task<A2AResponse> SendMessageAsync(MessageSendParams taskSendParams);
    Task<AgentTask> GetTaskAsync(string taskId);
    Task<AgentTask> CancelTaskAsync(TaskIdParams taskIdParams);
    IAsyncEnumerable<SseItem<A2AEvent>> SendMessageStreamAsync(MessageSendParams taskSendParams);
    IAsyncEnumerable<SseItem<A2AEvent>> ResubscribeToTaskAsync(string taskId);
    Task<TaskPushNotificationConfig> SetPushNotificationAsync(TaskPushNotificationConfig pushNotificationConfig);
    Task<TaskPushNotificationConfig> GetPushNotificationAsync(TaskIdParams taskIdParams);
}

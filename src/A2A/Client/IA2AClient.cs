using System.Net.ServerSentEvents;

namespace A2A;

public interface IA2AClient
{
    Task<A2AResponse> SendMessageAsync(MessageSendParams taskSendParams, CancellationToken cancellationToken = default);
    Task<AgentTask> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);
    Task<AgentTask> CancelTaskAsync(TaskIdParams taskIdParams, CancellationToken cancellationToken = default);
    IAsyncEnumerable<SseItem<A2AEvent>> SendMessageStreamAsync(MessageSendParams taskSendParams, CancellationToken cancellationToken = default);
    IAsyncEnumerable<SseItem<A2AEvent>> ResubscribeToTaskAsync(string taskId, CancellationToken cancellationToken = default);
    Task<TaskPushNotificationConfig> SetPushNotificationAsync(TaskPushNotificationConfig pushNotificationConfig, CancellationToken cancellationToken = default);
    Task<TaskPushNotificationConfig> GetPushNotificationAsync(TaskIdParams taskIdParams, CancellationToken cancellationToken = default);
}

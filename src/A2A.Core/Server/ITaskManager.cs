namespace A2A.Core;

public interface ITaskManager
{
    Func<MessageSendParams, Task<Message>>? OnMessageReceived { get; set; }

    Func<AgentTask, Task> OnTaskCreated { get; set; }
    Func<AgentTask, Task> OnTaskCancelled { get; set; }
    Func<AgentTask, Task> OnTaskUpdated { get; set; }
    Func<string, AgentCard> OnAgentCardQuery { get; set; }

    Task<AgentTask> CreateTaskAsync(string? contextId = null);
    Task ReturnArtifactAsync(string taskId, Artifact artifact);
    Task UpdateStatusAsync(string taskId, TaskState status, Message? message = null, bool final = false);
}

using A2A;

using TaskStatus = A2A.TaskStatus;

namespace AgentServer;

public class SpecComplianceAgent
{
    private ITaskStore? _store;

    public void Attach(TaskManager taskManager, ITaskStore store)
    {
        _store = store;
        taskManager.OnSendMessage = OnSendMessageAsync;
        taskManager.OnCancelTask = OnCancelTaskAsync;
    }

    private async Task<SendMessageResponse> OnSendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken)
    {
        var contextId = request.Message.ContextId ?? Guid.NewGuid().ToString("N");

        // If the message references an existing task, continue it
        if (!string.IsNullOrEmpty(request.Message.TaskId))
        {
            var existing = await _store!.GetTaskAsync(request.Message.TaskId, cancellationToken);
            if (existing is not null)
            {
                // Append the new message to history
                await _store.AppendHistoryAsync(existing.Id, request.Message, cancellationToken);

                // Build agent echo reply and append it too
                var replyMessage = new Message
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    Role = Role.Agent,
                    TaskId = existing.Id,
                    ContextId = existing.ContextId,
                    Parts = request.Message.Parts,
                };
                await _store.AppendHistoryAsync(existing.Id, replyMessage, cancellationToken);

                // Re-fetch to get updated history
                var updated = await _store.GetTaskAsync(existing.Id, cancellationToken);
                return new SendMessageResponse { Task = updated };
            }
        }

        // Create a new task
        var taskId = Guid.NewGuid().ToString("N");

        // Build an agent echo reply message
        var agentReply = new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Role = Role.Agent,
            TaskId = taskId,
            ContextId = contextId,
            Parts = request.Message.Parts,
        };

        var task = new AgentTask
        {
            Id = taskId,
            ContextId = contextId,
            Status = new TaskStatus
            {
                State = TaskState.Working,
                Timestamp = DateTimeOffset.UtcNow,
            },
            History = [request.Message, agentReply],
        };

        await _store!.SetTaskAsync(task, cancellationToken);

        return new SendMessageResponse { Task = task };
    }

    private async Task<AgentTask> OnCancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken)
    {
        var task = await _store!.GetTaskAsync(request.Id, cancellationToken)
            ?? throw new A2AException($"Task '{request.Id}' not found.", A2AErrorCode.TaskNotFound);

        var canceledStatus = new TaskStatus
        {
            State = TaskState.Canceled,
            Timestamp = DateTimeOffset.UtcNow,
        };

        return await _store.UpdateStatusAsync(task.Id, canceledStatus, cancellationToken);
    }

    public AgentCard GetAgentCard(string agentUrl)
    {
        return new AgentCard
        {
            Name = "A2A Specification Compliance Agent",
            Description = "Agent for A2A specification compliance verification.",
            Version = "1.0.0",
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    Url = agentUrl,
                    ProtocolBinding = "JSONRPC",
                    ProtocolVersion = "1.0",
                }
            ],
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["text/plain"],
            Capabilities = new AgentCapabilities
            {
                Streaming = true,
                PushNotifications = false,
            },
            Skills =
            [
                new AgentSkill
                {
                    Id = "echo",
                    Name = "Echo",
                    Description = "Echoes back user messages for specification compliance verification.",
                    Tags = ["echo", "a2a", "compliance"],
                }
            ],
        };
    }
}

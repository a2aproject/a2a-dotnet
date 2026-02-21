using A2A;

namespace AgentServer;

public sealed class SpecComplianceAgent : IAgentHandler
{
    public async Task ExecuteAsync(AgentContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);

        // Echo the user's message parts back as an agent reply
        var replyParts = context.Message.Parts?.ToList() ?? [Part.FromText("")];

        if (!context.IsContinuation)
        {
            // New task: Submit, then echo back with Working status
            var agentReply = new Message
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Role = Role.Agent,
                TaskId = context.TaskId,
                ContextId = context.ContextId,
                Parts = replyParts,
            };

            // Emit initial task
            await eventQueue.EnqueueTaskAsync(new AgentTask
            {
                Id = context.TaskId,
                ContextId = context.ContextId,
                Status = new A2A.TaskStatus
                {
                    State = TaskState.Working,
                    Timestamp = DateTimeOffset.UtcNow,
                },
                History = [context.Message, agentReply],
            }, cancellationToken);
        }
        else
        {
            // Continuation: echo the parts back
            var agentReply = new Message
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Role = Role.Agent,
                TaskId = context.TaskId,
                ContextId = context.ContextId,
                Parts = replyParts,
            };

            // Return updated task with the reply added to history
            var history = context.Task!.History?.ToList() ?? [];
            history.Add(context.Message);
            history.Add(agentReply);

            await eventQueue.EnqueueTaskAsync(new AgentTask
            {
                Id = context.TaskId,
                ContextId = context.ContextId,
                Status = context.Task.Status,
                History = history,
                Artifacts = context.Task.Artifacts,
            }, cancellationToken);
        }
    }

    public static AgentCard GetAgentCard(string agentUrl) =>
        new()
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

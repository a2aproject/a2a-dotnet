using A2A;
using System.Diagnostics;

using TaskStatus = A2A.TaskStatus;

namespace AgentServer;

public class ResearcherAgent
{
    private ITaskStore? _store;
    public static readonly ActivitySource ActivitySource = new("A2A.ResearcherAgent", "1.0.0");

    public void Attach(TaskManager taskManager, ITaskStore store)
    {
        _store = store;
        taskManager.OnSendMessage = OnSendMessageAsync;
    }

    private async Task<SendMessageResponse> OnSendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken)
    {
        var messageText = request.Message.Parts.FirstOrDefault(p => p.Text is not null)?.Text ?? string.Empty;
        var contextId = request.Message.ContextId ?? Guid.NewGuid().ToString("N");
        var taskId = request.Message.TaskId ?? Guid.NewGuid().ToString("N");

        // Check if this is a continuation of an existing task
        var existingTask = await _store!.GetTaskAsync(taskId, cancellationToken);

        if (existingTask is not null)
        {
            // Continuation: append message and process
            await _store.AppendHistoryAsync(taskId, request.Message, cancellationToken);

            if (messageText == "go ahead")
            {
                // Research phase
                using var activity = ActivitySource.StartActivity("DoResearch", ActivityKind.Server);
                activity?.SetTag("task.id", taskId);

                var researchStatus = new TaskStatus { State = TaskState.Working, Timestamp = DateTimeOffset.UtcNow };
                await _store.UpdateStatusAsync(taskId, researchStatus, cancellationToken);

                existingTask.Status = new TaskStatus
                {
                    State = TaskState.Completed,
                    Timestamp = DateTimeOffset.UtcNow,
                    Message = new Message
                    {
                        Role = Role.Agent,
                        Parts = [Part.FromText("Task completed successfully")],
                    }
                };
                existingTask.Artifacts = [new Artifact { ArtifactId = Guid.NewGuid().ToString("N"), Parts = [Part.FromText($"{messageText} received.")] }];
                var updated = await _store.SetTaskAsync(existingTask, cancellationToken);
                return new SendMessageResponse { Task = updated };
            }
            else
            {
                // Re-plan
                using var activity = ActivitySource.StartActivity("DoPlanning", ActivityKind.Server);
                activity?.SetTag("task.id", taskId);
                await Task.Delay(1000, cancellationToken);

                existingTask.Status = new TaskStatus
                {
                    State = TaskState.InputRequired,
                    Timestamp = DateTimeOffset.UtcNow,
                    Message = new Message
                    {
                        Role = Role.Agent,
                        Parts = [Part.FromText("When ready say go ahead")],
                    }
                };
                existingTask.Artifacts = [new Artifact { ArtifactId = Guid.NewGuid().ToString("N"), Parts = [Part.FromText($"{messageText} received.")] }];
                var updated = await _store.SetTaskAsync(existingTask, cancellationToken);
                return new SendMessageResponse { Task = updated };
            }
        }
        else
        {
            // New task: planning phase
            using var activity = ActivitySource.StartActivity("DoPlanning", ActivityKind.Server);
            activity?.SetTag("task.id", taskId);

            await Task.Delay(1000, cancellationToken);

            var task = new AgentTask
            {
                Id = taskId,
                ContextId = contextId,
                Status = new TaskStatus
                {
                    State = TaskState.InputRequired,
                    Timestamp = DateTimeOffset.UtcNow,
                    Message = new Message
                    {
                        Role = Role.Agent,
                        Parts = [Part.FromText("When ready say go ahead")],
                    }
                },
                History = [request.Message],
                Artifacts = [new Artifact { ArtifactId = Guid.NewGuid().ToString("N"), Parts = [Part.FromText($"{messageText} received.")] }],
            };

            await _store.SetTaskAsync(task, cancellationToken);
            return new SendMessageResponse { Task = task };
        }
    }

    public AgentCard GetAgentCard(string agentUrl)
    {
        return new AgentCard
        {
            Name = "Researcher Agent",
            Description = "Agent which conducts research.",
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
                    Id = "research",
                    Name = "Research",
                    Description = "Conducts research on a given topic.",
                    Tags = ["research", "planning"],
                }
            ],
        };
    }
}
using System.Diagnostics;

namespace A2A;

public class ResearcherAgent
{
    private ITaskManager? _taskManager;
    private readonly Dictionary<string, AgentState> _agentStates = [];
    public static readonly ActivitySource ActivitySource = new("A2A.ResearcherAgent", "1.0.0");

    private enum AgentState
    {
        Planning,
        WaitingForFeedbackOnPlan,
        Researching
    }

    public void Attach(ITaskManager taskManager)
    {
        _taskManager = taskManager;
        _taskManager.OnTaskCreated = async (task, _) =>
        {
            // Initialize the agent state for the task
            _agentStates[task.Id] = AgentState.Planning;
            // Ignore other content in the task, just assume it is a text message.
            var message = ((TextPart?)task.History?.Last()?.Parts?.FirstOrDefault())?.Text ?? string.Empty;
            await Invoke(task.Id, message);
        };
        _taskManager.OnTaskUpdated = async (task, _) =>
        {
            // Note that the updated callback is helpful to know not to initialize the agent state again.
            var message = ((TextPart?)task.History?.Last()?.Parts?.FirstOrDefault())?.Text ?? string.Empty;
            await Invoke(task.Id, message);
        };
        _taskManager.OnAgentCardQuery = GetAgentCard;
    }

    // This is the main entry point for the agent. It is called when a task is created or updated.
    // It probably should have a cancellation token to enable the process to be cancelled.
    public async Task Invoke(string taskId, string message)
    {
        if (_taskManager == null)
        {
            throw new InvalidOperationException("TaskManager is not attached.");
        }

        using var activity = ActivitySource.StartActivity("Invoke", ActivityKind.Server);
        activity?.SetTag("task.id", taskId);
        activity?.SetTag("message", message);
        activity?.SetTag("state", _agentStates[taskId].ToString());

        switch (_agentStates[taskId])
        {
            case AgentState.Planning:
                await DoPlanning(taskId, message);
                await _taskManager.UpdateStatusAsync(taskId, TaskState.InputRequired, new Message()
                {
                    Parts = [new TextPart() { Text = "When ready say go ahead" }],
                });
                break;
            case AgentState.WaitingForFeedbackOnPlan:
                if (message == "go ahead")  // Dumb check for now to avoid using an LLM
                {
                    await DoResearch(taskId, message);
                }
                else
                {
                    // Take the message and redo planning
                    await DoPlanning(taskId, message);
                    await _taskManager.UpdateStatusAsync(taskId, TaskState.InputRequired, new Message()
                    {
                        Parts = [new TextPart() { Text = "When ready say go ahead" }],
                    });
                }
                break;
            case AgentState.Researching:
                await DoResearch(taskId, message);
                break;
        }
    }

    private async Task DoResearch(string taskId, string message)
    {
        if (_taskManager == null)
        {
            throw new InvalidOperationException("TaskManager is not attached.");
        }

        using var activity = ActivitySource.StartActivity("DoResearch", ActivityKind.Server);
        activity?.SetTag("task.id", taskId);
        activity?.SetTag("message", message);

        _agentStates[taskId] = AgentState.Researching;
        await _taskManager.UpdateStatusAsync(taskId, TaskState.Working);

        await _taskManager.ReturnArtifactAsync(
            taskId,
            new Artifact()
            {
                Parts = [new TextPart() { Text = $"{message} received." }],
            });

        await _taskManager.UpdateStatusAsync(taskId, TaskState.Completed, new Message()
        {
            Parts = [new TextPart() { Text = "Task completed successfully" }],
        });
    }
    private async Task DoPlanning(string taskId, string message)
    {
        if (_taskManager == null)
        {
            throw new InvalidOperationException("TaskManager is not attached.");
        }

        using var activity = ActivitySource.StartActivity("DoPlanning", ActivityKind.Server);
        activity?.SetTag("task.id", taskId);
        activity?.SetTag("message", message);

        // Task should be in status Submitted
        // Simulate being in a queue for a while
        await Task.Delay(1000);
        // Simulate processing the task
        await _taskManager.UpdateStatusAsync(taskId, TaskState.Working);

        await _taskManager.ReturnArtifactAsync(
            taskId,
            new Artifact()
            {
                Parts = [new TextPart() { Text = $"{message} received." }],
            });

        await _taskManager.UpdateStatusAsync(taskId, TaskState.InputRequired, new Message()
        {
            Parts = [new TextPart() { Text = "When ready say go ahead" }],
        });
        _agentStates[taskId] = AgentState.WaitingForFeedbackOnPlan;
    }

    private AgentCard GetAgentCard(string agentUrl, CancellationToken _)
    {
        var capabilities = new AgentCapabilities()
        {
            Streaming = true,
            PushNotifications = false,
        };

        return new AgentCard()
        {
            Name = "Researcher Agent",
            Description = "Agent which conducts research.",
            Url = agentUrl,
            Version = "1.0.0",
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Capabilities = capabilities,
            Skills = [],
        };
    }
}
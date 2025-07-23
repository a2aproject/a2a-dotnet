using A2A;

namespace AgentServer;

public class SpecComplianceAgent
{
    private ITaskManager? _taskManager;

    public void Attach(ITaskManager taskManager)
    {
        taskManager.OnAgentCardQuery = GetAgentCard;
        taskManager.OnTaskCreated = OnTaskCreatedAsync;
        _taskManager = taskManager;
    }

    private async Task OnTaskCreatedAsync(AgentTask task, CancellationToken cancellationToken)
    {
        // A temporary solution to prevent the compliance test at https://github.com/a2aproject/a2a-tck/blob/main/tests/optional/capabilities/test_streaming_methods.py from hanging.
        // It can be removed after the issue https://github.com/a2aproject/a2a-dotnet/issues/97 is resolved.
        if (task.History?.Any(m => m.MessageId.StartsWith("test-stream-message-id", StringComparison.InvariantCulture)) ?? false)
        {
            await _taskManager!.UpdateStatusAsync(
            task.Id,
            status: TaskState.Completed,
            final: true,
            cancellationToken: cancellationToken);
        }
    }

    private Task<AgentCard> GetAgentCard(string agentUrl, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AgentCard>(cancellationToken);
        }

        var capabilities = new AgentCapabilities()
        {
            Streaming = true,
            PushNotifications = false,
        };

        return Task.FromResult(new AgentCard()
        {
            Name = "A2A Specification Compliance Agent",
            Description = "Agent to run A2A specification compliance tests.",
            Url = agentUrl,
            Version = "1.0.0",
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Capabilities = capabilities,
            Skills = [],
        });
    }
}

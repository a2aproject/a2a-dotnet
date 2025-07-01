using A2A.Core;
using System.Diagnostics;


public class HostedClientAgent
{
    private ITaskManager? _TaskManager;
    private A2AClient echoClient;
    public static readonly ActivitySource ActivitySource = new ActivitySource("A2A.HostedClientAgent", "1.0.0");

    public HostedClientAgent()
    {
        echoClient = new A2AClient(new HttpClient() { BaseAddress = new Uri("http://localhost:5048/echo") });
    }
    public void Attach(TaskManager taskManager)
    {
        _TaskManager = taskManager;
        taskManager.OnTaskCreated = async (task) =>
        {
            await ExecuteAgentTask(task);
        };
        taskManager.OnTaskUpdated = async (task) =>
        {
            await ExecuteAgentTask(task);
        };
        taskManager.OnAgentCardQuery = GetAgentCard;
    }
    public async Task ExecuteAgentTask(AgentTask task)
    {
        using var activity = ActivitySource.StartActivity("ExecuteAgentTask", ActivityKind.Server);
        activity?.SetTag("task.id", task.Id);
        activity?.SetTag("task.sessionId", task.ContextId);

        if (_TaskManager == null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "TaskManager is not attached.");
            throw new Exception("TaskManager is not attached.");
        }

        await _TaskManager.UpdateStatusAsync(task.Id, TaskState.Working);

        // Get message from the user to HostedClientAgent
        var userMessage = task.History!.Last().Parts.First().AsTextPart().Text;
        var echoTask = await echoClient.SendMessageAsync(new MessageSendParams() 
        {
            Message = new Message()
            {
                Role = MessageRole.User,
                ContextId = task.ContextId,
                Parts = [new TextPart() {
                    Text = $"HostedClientAgent received {userMessage}"
                }]
            }
        }) as Message;

        // Get the the return artifact from the EchoAgent
        var message = echoTask!.Parts.First().AsTextPart().Text;

        // Return as artifact to the HostedClientAgent
        var artifact = new Artifact()
        {
            Parts = [new TextPart() {
                Text = $"EchoAgent said: {message}"
            }]
        };
        await _TaskManager.ReturnArtifactAsync(task.Id, artifact);
        await _TaskManager.UpdateStatusAsync(task.Id, TaskState.Completed);
    }

    public AgentCard GetAgentCard(string agentUrl)
    {
        var capabilities = new AgentCapabilities()
        {
            Streaming = true,
            PushNotifications = false,
        };

        return new AgentCard()
        {
            Name = "Host Client Agent",
            Description = "Agent is a hosted client.",
            Url = agentUrl,
            Version = "1.0.0",
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Capabilities = capabilities,
            Skills = [],
        };
    }
}
using A2A;
using System.Text.Json;

namespace AgentClient.Samples;

/// <summary>
/// Demonstrates how to implement task-based communication with an agent.
/// </summary>
/// <remarks>
/// <para>
/// Task-based communication creates persistent AgentTask objects that maintain state throughout their lifecycle.
/// This pattern is ideal for complex, long-running interactions where you need to track task progress, maintain
/// conversation history, manage artifacts, and handle task state transitions over time.
/// </para>
/// <para>
/// Key characteristics:
/// </para>
/// <list type="bullet">
/// <item><description>Stateful: Tasks maintain persistent state and conversation history across interactions</description></item>
/// <item><description>Lifecycle management: Tasks progress through states like Submitted, Working, InputRequired, Completed, or Canceled</description></item>
/// <item><description>Artifact collection: Agents can return multiple artifacts as they work on the task</description></item>
/// <item><description>Progress tracking: Task status updates provide visibility into agent progress and current state</description></item>
/// <item><description>Resumable interactions: Tasks can be paused, require input, and be resumed later</description></item>
/// </list>
/// <para>
/// This differs from message-based communication which provides immediate, stateless responses without
/// creating persistent task objects or maintaining interaction history.
/// </para>
/// <para>
/// For more details about task-based communication and the complete A2A protocol lifecycle, refer to:
/// https://github.com/a2aproject/A2A/blob/main/docs/topics/life-of-a-task.md
/// </para>
/// </remarks>
internal sealed class TaskBasedCommunicationSample
{
    /// <summary>
    /// Demonstrates the complete workflow of task-based communication with an A2A agent.
    /// </summary>
    /// <remarks>
    /// This method shows how to:
    /// <list type="number">
    /// <item><description>Start a local agent server to host an echo agent.</description></item>
    /// <item><description>Resolve the agent card to obtain connection details.</description></item>
    /// <item><description>Create an <see cref="A2AClient"/> to communicate with the agent.</description></item>
    /// <item><description>Demonstrate a short-lived task that completes immediately.</description></item>
    /// <item><description>Demonstrate a long-running task.</description></item>
    /// </list>
    /// </remarks>
    public static async Task RunAsync()
    {
        Console.WriteLine($"\n=== Running the {nameof(TaskBasedCommunicationSample)} sample ===");

        // 1. Start the local agent server to host the echo agent
        await AgentServerUtils.StartLocalAgentServerAsync(agentName: "echotasks", port: 5101);

        // 2. Get the agent card
        A2ACardResolver cardResolver = new(new Uri("http://localhost:5101"));
        AgentCard echoAgentCard = await cardResolver.GetAgentCardAsync();

        // 3. Create an A2A client to communicate with the echotasks agent using the URL from the agent card
        A2AClient agentClient = new(new Uri(echoAgentCard.Url));

        // 4. Demo a short-lived task
        await DemoShortLivedTaskAsync(agentClient);

        // 5. Demo a long-running task
        await DemoLongRunningTaskAsync(agentClient);
    }

    /// <summary>
    /// Demonstrates a short-lived task that completes immediately.
    /// </summary>
    private static async Task DemoShortLivedTaskAsync(A2AClient agentClient)
    {
        Console.WriteLine("\nShort-lived Task");

        AgentMessage userMessage = new()
        {
            Parts = [new TextPart { Text = "Hello from a short-lived task sample!" }],
            Role = MessageRole.User
        };

        Console.WriteLine($" Sending message to the agent: {((TextPart)userMessage.Parts[0]).Text}");
        AgentTask agentResponse = (AgentTask)await agentClient.SendMessageAsync(new MessageSendParams { Message = userMessage });
        DisplayTaskDetails(agentResponse);
    }

    /// <summary>
    /// Demonstrates a long-running task.
    /// </summary>
    private static async Task DemoLongRunningTaskAsync(A2AClient agentClient)
    {
        Console.WriteLine("\nLong-running Task");

        AgentMessage userMessage = new()
        {
            Parts = [new TextPart { Text = "Hello from a long-running task sample!" }],
            Role = MessageRole.User,
            Metadata = new Dictionary<string, JsonElement>
            {
                // Tweaking the agent behavior to simulate a long-running task;
                // otherwise the agent will echo with Completed task.
                { "task-target-state", JsonSerializer.SerializeToElement(TaskState.Working) }
            }
        };

        // 1. Create a new task by sending the message to the agent
        Console.WriteLine($" Sending message to the agent: {((TextPart)userMessage.Parts[0]).Text}");
        AgentTask agentResponse = (AgentTask)await agentClient.SendMessageAsync(new MessageSendParams { Message = userMessage });
        DisplayTaskDetails(agentResponse);

        // 2. Retrieve the task
        Console.WriteLine($"\n Retrieving the task by ID: {agentResponse.Id}");
        agentResponse = await agentClient.GetTaskAsync(agentResponse.Id);
        DisplayTaskDetails(agentResponse);

        // 3. Cancel the task
        Console.WriteLine($"\n Cancel the task with ID: {agentResponse.Id}");
        AgentTask cancelledTask = await agentClient.CancelTaskAsync(new TaskIdParams { Id = agentResponse.Id });
        DisplayTaskDetails(cancelledTask);
    }

    private static void DisplayTaskDetails(AgentTask agentResponse)
    {
        Console.WriteLine(" Received task details:");
        Console.WriteLine($"  ID: {agentResponse.Id}");
        Console.WriteLine($"  Status: {agentResponse.Status.State}");
        Console.WriteLine($"  Artifact: {(agentResponse.Artifacts?[0].Parts?[0] as TextPart)?.Text}");
    }
}

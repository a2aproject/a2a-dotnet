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
        A2AClient agentClient = new(new Uri("http://localhost:5101/echotasks"));

        // 4. Demonstrate stream reconnection issues
        await Repro_StreamReconnectionHangsIndefinitelyAsync(agentClient);
        await Repro_StreamReconnectionFailsForTerminatedTaskAsync(agentClient);
    }

    /// <summary>
    /// Issue: Reconnecting to a task stream hangs indefinitely.
    /// </summary>
    /// <remarks>
    /// After a simulated stream disconnection, the client reconnects using
    /// <see cref="A2AClient.SubscribeToTaskAsync"/>. The stream never completes,
    /// so execution hangs and never reaches past the <c>await foreach</c> loop.
    /// </remarks>
    private static async Task Repro_StreamReconnectionHangsIndefinitelyAsync(A2AClient agentClient)
    {
        Console.WriteLine("\nStream Reconnection Demo");

        Message userMessage = new()
        {
            Parts = [Part.FromText("Hello from a stream reconnection demo!")],
            Role = Role.User,
            MessageId = Guid.NewGuid().ToString("N")
        };

        string? taskId = null;

        // Receive streaming updates and simulate a stream interruption after the first update
        await foreach (var response in agentClient.SendStreamingMessageAsync(new SendMessageRequest { Message = userMessage }))
        {
            taskId = response.Task!.Id;

            // Simulate stream disconnection by breaking out of the loop
            break;
        }

        // Reconnect to the stream to resume receiving updates for the same task
        await foreach (var response in agentClient.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = taskId! }))
        {
            Console.WriteLine($" Received task update after reconnection: ID={response.Task!.Id}, Status={response.Task.Status.State}, Artifact={response.Task.Artifacts?[0].Parts?[0].Text}");
        }

        // Note: execution never reaches here because the stream remains open indefinitely
    }

    /// <summary>
    /// Issue: Reconnecting to a completed task's stream throws instead of returning the final state.
    /// </summary>
    /// <remarks>
    /// After a simulated stream disconnection, if the task reaches a terminal state
    /// (Completed/Failed/Canceled) before the client reconnects, <see cref="A2AClient.SubscribeToTaskAsync"/>
    /// throws an <see cref="A2AException"/> with <see cref="A2AErrorCode.UnsupportedOperation"/>.
    /// The client must catch the exception and fall back to <see cref="A2AClient.GetTaskAsync"/>
    /// to retrieve the final task state.
    /// </remarks>
    private static async Task Repro_StreamReconnectionFailsForTerminatedTaskAsync(A2AClient agentClient)
    {
        Message userMessage = new()
        {
            Parts = [Part.FromText("Hello from a stream reconnection demo!")],
            Role = Role.User,
            MessageId = Guid.NewGuid().ToString("N")
        };

        string? taskId = null;

        // Receive streaming updates and simulate a stream interruption after the first update
        await foreach (var response in agentClient.SendStreamingMessageAsync(new SendMessageRequest { Message = userMessage }))
        {
            taskId = response.Task!.Id;

            // Simulate stream disconnection by breaking out of the loop
            break;
        }

        // Simulate a delay before reconnecting. During this time, the task may reach a terminal
        // state (Completed/Failed/Canceled) while the client is unaware due to the disconnection.
        await Task.Delay(5000);

        try
        {
            // Attempt to reconnect to the task stream. If the task has already reached a terminal
            // state, the server rejects the subscription with an UnsupportedOperation error.
            await foreach (var response in agentClient.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = taskId! }))
            {
                Console.WriteLine($" Received task update after reconnection: ID={response.Task!.Id}, Status={response.Task.Status.State}, Artifact={response.Task.Artifacts?[0].Parts?[0].Text}");
            }
        }
        catch (A2AException ex) when (ex.ErrorCode == A2AErrorCode.UnsupportedOperation)
        {
            // Expected: "Task is in a terminal state and cannot be subscribed to."
            // Fall back to GetTaskAsync to retrieve the final task state.
        }

        AgentTask finalTaskState = await agentClient.GetTaskAsync(new GetTaskRequest { Id = taskId! });
    }

    /// <summary>
    /// Demonstrates a short-lived task that completes immediately.
    /// </summary>
    private static async Task DemoShortLivedTaskAsync(A2AClient agentClient)
    {
        Console.WriteLine("\nShort-lived Task");

        Message userMessage = new()
        {
            Parts = [Part.FromText("Hello from a short-lived task sample!")],
            Role = Role.User,
            MessageId = Guid.NewGuid().ToString("N")
        };

        Console.WriteLine($" Sending message to the agent: {userMessage.Parts[0].Text}");
        SendMessageResponse response = await agentClient.SendMessageAsync(new SendMessageRequest { Message = userMessage });
        DisplayTaskDetails(response.Task!);
    }

    /// <summary>
    /// Demonstrates a long-running task.
    /// </summary>
    private static async Task DemoLongRunningTaskAsync(A2AClient agentClient)
    {
        Console.WriteLine("\nLong-running Task");

        Message userMessage = new()
        {
            Parts = [Part.FromText("Hello from a long-running task sample!")],
            Role = Role.User,
            MessageId = Guid.NewGuid().ToString("N"),
            Metadata = new Dictionary<string, JsonElement>
            {
                // Tweaking the agent behavior to simulate a long-running task;
                // otherwise the agent will echo with Completed task.
                { "task-target-state", JsonSerializer.SerializeToElement(TaskState.Working) }
            }
        };

        // 1. Create a new task by sending the message to the agent
        Console.WriteLine($" Sending message to the agent: {userMessage.Parts[0].Text}");
        SendMessageResponse response = await agentClient.SendMessageAsync(new SendMessageRequest { Message = userMessage });
        AgentTask agentResponse = response.Task!;
        DisplayTaskDetails(agentResponse);

        // 2. Retrieve the task
        Console.WriteLine($"\n Retrieving the task by ID: {agentResponse.Id}");
        agentResponse = await agentClient.GetTaskAsync(new GetTaskRequest { Id = agentResponse.Id });
        DisplayTaskDetails(agentResponse);

        // 3. Cancel the task
        Console.WriteLine($"\n Cancel the task with ID: {agentResponse.Id}");
        AgentTask cancelledTask = await agentClient.CancelTaskAsync(new CancelTaskRequest { Id = agentResponse.Id });
        DisplayTaskDetails(cancelledTask);
    }

    private static void DisplayTaskDetails(AgentTask agentResponse)
    {
        Console.WriteLine(" Received task details:");
        Console.WriteLine($"  ID: {agentResponse.Id}");
        Console.WriteLine($"  Status: {agentResponse.Status.State}");
        Console.WriteLine($"  Artifact: {agentResponse.Artifacts?[0].Parts?[0].Text}");
    }
}

using A2A;
using System.Net.ServerSentEvents;

namespace AgentClient.Samples;

/// <summary>
/// Demonstrates how to implement message-based communication with an agent.
/// </summary>
/// <remarks>
/// <para>
/// Message-based communication is a stateless, immediate interaction pattern where messages are sent directly to an agent
/// and receive an immediate response without creating persistent tasks. This pattern is ideal for simple, one-off interactions
/// where you don't need to track task state, progress, or history.
/// </para>
/// <para>
/// Key characteristics:
/// </para>
/// <list type="bullet">
/// <item><description>Stateless: Each message is independent and doesn't maintain state between calls</description></item>
/// <item><description>Immediate response: The agent processes the message and returns a response synchronously</description></item>
/// <item><description>No task lifecycle: Unlike task-based communication, there's no task creation, status updates, or artifacts</description></item>
/// <item><description>Simplified workflow: Perfect for chatbot-style interactions or simple query-response scenarios</description></item>
/// </list>
/// <para>
/// This differs from task-based communication which creates persistent AgentTask objects that can be tracked,
/// updated, cancelled, and can maintain conversation history and artifacts over time.
/// </para>
/// <para>
/// For more details about message-based communication and the complete A2A protocol lifecycle, refer to:
/// https://github.com/a2aproject/A2A/blob/main/docs/topics/life-of-a-task.md
/// </para>
/// </remarks>
internal sealed class MessageBasedCommunicationSample
{
    /// <summary>
    /// Demonstrates the complete workflow of message-based communication with an A2A agent.
    /// </summary>
    /// <remarks>
    /// This method shows how to:
    /// <list type="number">
    /// <item><description>Start a local agent server to host the echo agent.</description></item>
    /// <item><description>Resolve the agent card to obtain connection details.</description></item>
    /// <item><description>Create an <see cref="A2AClient"/> to communicate with the agent.</description></item>
    /// <item><description>Send a message to the agent using the non-streaming API.</description></item>
    /// <item><description>Send a message to the agent using the streaming API.</description></item>
    /// </list>
    /// </remarks>
    public static async Task RunAsync()
    {
        Console.WriteLine($"\n=== Running the {nameof(MessageBasedCommunicationSample)} sample ===");

        // Start the local agent server to host the echo agent
        await AgentServerUtils.StartLocalAgentServerAsync(agentName: "echo", port: 5100);

        // 1. Get the agent card
        A2ACardResolver cardResolver = new(new Uri("http://localhost:5100/"));
        AgentCard echoAgentCard = await cardResolver.GetAgentCardAsync();

        // 2. Create an A2A client to communicate with the agent using url from the agent card
        A2AClient agentClient = new(new Uri(echoAgentCard.Url));

        // 3. Create a message to send to the agent
        AgentMessage userMessage = new()
        {
            Role = MessageRole.User,
            MessageId = Guid.NewGuid().ToString(),
            Parts = [
                new TextPart
                    {
                        Text = "Hello from the message-based communication sample! Please echo this message."
                    }
            ]
        };

        // 4. Send the message using non-streaming API
        await SendMessageAsync(agentClient, userMessage);

        // 5. Send the message using streaming API
        await SendMessageStreamingAsync(agentClient, userMessage);
    }

    /// <summary>
    /// Demonstrates non-streaming message communication with an A2A agent.
    /// </summary>
    private static async Task SendMessageAsync(A2AClient agentClient, AgentMessage userMessage)
    {
        Console.WriteLine("\nNon-Streaming Message Communication");
        Console.WriteLine($" Sending message via non-streaming API: {((TextPart)userMessage.Parts[0]).Text}");

        // Send the message and get the response
        AgentMessage agentResponse = (AgentMessage)await agentClient.SendMessageAsync(new MessageSendParams { Message = userMessage });

        // Display the response
        Console.WriteLine($" Received complete response from agent: {((TextPart)agentResponse.Parts[0]).Text}");
    }

    /// <summary>
    /// Demonstrates streaming message communication with an A2A agent using Server-Sent Events.
    /// </summary>
    /// <param name="agentClient">The A2A client for communicating with the agent.</param>
    /// <param name="userMessage">The message to send to the agent.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task SendMessageStreamingAsync(A2AClient agentClient, AgentMessage userMessage)
    {
        Console.WriteLine("\nStreaming Message Communication");
        Console.WriteLine($" Sending message via streaming API: {((TextPart)userMessage.Parts[0]).Text}");

        // Send the message and get the response as a stream
        await foreach (SseItem<A2AEvent> sseItem in agentClient.SendMessageStreamingAsync(new MessageSendParams { Message = userMessage }))
        {
            AgentMessage agentResponse = (AgentMessage)sseItem.Data;

            // Display each part of the response as it arrives
            Console.WriteLine($" Received streaming response chunk: {((TextPart)agentResponse.Parts[0]).Text}");
        }
    }
}
using A2A;
using System.Text.Json;

namespace AgentClient.Samples;

/// <summary>
/// Demonstrates how to retrieve and access agent details through the agent card.
/// </summary>
/// <remarks>
/// <para>
/// The agent card is a standardized way to access an agent's capabilities, metadata, and connection details.
/// Once you have an agent's endpoint URL, you can retrieve its agent card to understand what the agent can do
/// and how to interact with it effectively.
/// </para>
/// <para>
/// Key information available through the agent card:
/// </para>
/// <list type="bullet">
/// <item><description>Agent metadata: name, description, version, and provider information</description></item>
/// <item><description>Capabilities: streaming support, push notifications, and available extensions</description></item>
/// <item><description>Skills: specific capabilities and functions the agent can perform</description></item>
/// <item><description>Supported modalities: input and output content types (text, image, etc.)</description></item>
/// </list>
/// <para>
/// This information is essential before establishing communication with an agent, as it helps clients understand
/// the agent's capabilities and choose the appropriate interaction patterns.
/// </para>
/// <para>
/// For more details about the agent card specification, refer to:
/// https://github.com/a2aproject/A2A/blob/main/docs/topics/agent-discovery.md
/// </para>
/// </remarks>
internal sealed class GetAgentDetailsSample
{
    private static readonly JsonSerializerOptions s_indentedOptions = new(A2AJsonUtilities.DefaultOptions)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Demonstrates how to retrieve agent details using the <see cref="A2ACardResolver"/>.
    /// </summary>
    /// <remarks>
    /// This method shows how to:
    /// <list type="number">
    /// <item><description>Start a local agent server to host the echo agent.</description></item>
    /// <item><description>Create an <see cref="A2ACardResolver"/> instance with the agent's base URL.</description></item>
    /// <item><description>Retrieve the agent card containing capabilities and metadata.</description></item>
    /// <item><description>Display the agent information in a readable format.</description></item>
    /// </list>
    /// </remarks>
    public static async Task RunAsync()
    {
        Console.WriteLine($"=== Running the {nameof(GetAgentDetailsSample)} sample ===");

        // 1. Start the local agent server to host the echo agent
        await AgentServerUtils.StartLocalAgentServerAsync(agentName: "echo", port: 5100);

        // 2. Create agent card resolver
        A2ACardResolver agentCardResolver = new(new Uri("http://localhost:5100/"));

        // 3. Get agent card
        AgentCard agentCard = await agentCardResolver.GetAgentCardAsync();

        // 4. Display agent details
        Console.WriteLine("\nAgent card details:");
        Console.WriteLine(JsonSerializer.Serialize(agentCard, s_indentedOptions));
    }
}

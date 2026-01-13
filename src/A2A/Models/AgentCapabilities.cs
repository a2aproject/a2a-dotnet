using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Defines optional capabilities supported by an agent.
/// </summary>
public sealed class AgentCapabilities
{
    /// <summary>
    /// Creates a new instance of the <see cref="AgentCapabilities"/> class.
    /// </summary>
    public AgentCapabilities()
    {
    }

    internal AgentCapabilities(AgentCapabilities source)
    {
        Streaming = source.Streaming;
        PushNotifications = source.PushNotifications;
        StateTransitionHistory = source.StateTransitionHistory;
        Extensions = [.. source.Extensions];
    }

    /// <summary>
    /// Gets or sets a value indicating whether the agent supports SSE.
    /// </summary>
    [JsonPropertyName("streaming")]
    public bool Streaming { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the agent can notify updates to client.
    /// </summary>
    [JsonPropertyName("pushNotifications")]
    public bool PushNotifications { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the agent exposes status change history for tasks.
    /// </summary>
    [JsonPropertyName("stateTransitionHistory")]
    public bool StateTransitionHistory { get; set; }

    /// <summary>
    /// Extensions supported by this agent.
    /// </summary>
    [JsonPropertyName("extensions")]
    public List<AgentExtension> Extensions { get; set; } = [];
}

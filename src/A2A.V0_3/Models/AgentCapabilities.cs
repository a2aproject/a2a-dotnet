using System.Text.Json.Serialization;

namespace A2A.V0_3;

/// <summary>
/// Defines optional capabilities supported by an agent.
/// </summary>
public sealed class AgentCapabilities
{
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

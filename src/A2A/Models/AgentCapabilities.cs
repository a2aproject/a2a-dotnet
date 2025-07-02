using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Capabilities of an agent
/// </summary>
public class AgentCapabilities
{
    /// <summary>
    /// Whether the agent supports streaming
    /// </summary>
    [JsonPropertyName("streaming")]
    public bool Streaming { get; set; }

    /// <summary>
    /// Whether the agent supports push notifications
    /// </summary>
    [JsonPropertyName("pushNotifications")]
    public bool PushNotifications { get; set; }

    /// <summary>
    /// Whether the agent supports state transition history
    /// </summary>
    [JsonPropertyName("stateTransitionHistory")]
    public bool StateTransitionHistory { get; set; }
}

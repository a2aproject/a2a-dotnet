namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents the capabilities of an agent.</summary>
public sealed class AgentCapabilities
{
    /// <summary>Gets or sets whether the agent supports streaming.</summary>
    public bool? Streaming { get; set; }

    /// <summary>Gets or sets whether the agent supports push notifications.</summary>
    public bool? PushNotifications { get; set; }

    /// <summary>Gets or sets the extensions supported by this agent.</summary>
    public List<AgentExtension>? Extensions { get; set; }

    /// <summary>Gets or sets whether the agent supports extended agent card.</summary>
    public bool? ExtendedAgentCard { get; set; }
}

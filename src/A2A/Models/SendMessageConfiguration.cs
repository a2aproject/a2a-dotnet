namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents configuration options for a send message request.</summary>
public sealed class SendMessageConfiguration
{
    /// <summary>Gets or sets the accepted output modes.</summary>
    public List<string>? AcceptedOutputModes { get; set; }

    /// <summary>Gets or sets the push notification configuration.</summary>
    public PushNotificationConfig? PushNotificationConfig { get; set; }

    /// <summary>Gets or sets the history length to include.</summary>
    public int? HistoryLength { get; set; }

    /// <summary>Gets or sets whether to return immediately without waiting for task completion.</summary>
    public bool ReturnImmediately { get; set; }
}

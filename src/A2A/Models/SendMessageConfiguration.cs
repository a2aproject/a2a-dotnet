namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents configuration options for a send message request.</summary>
public sealed class SendMessageConfiguration
{
    /// <summary>Gets or sets the accepted output modes.</summary>
    [JsonPropertyName("acceptedOutputModes")]
    public List<string>? AcceptedOutputModes { get; set; }

    /// <summary>Gets or sets the push notification configuration.</summary>
    [JsonPropertyName("pushNotificationConfig")]
    public PushNotificationConfig? PushNotificationConfig { get; set; }

    /// <summary>Gets or sets the history length to include.</summary>
    [JsonPropertyName("historyLength")]
    public int? HistoryLength { get; set; }

    /// <summary>Gets or sets whether the request is blocking.</summary>
    [JsonPropertyName("blocking")]
    public bool Blocking { get; set; }
}

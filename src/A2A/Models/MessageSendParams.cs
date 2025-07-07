using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Parameters for sending a message request to an agent.
/// </summary>
/// <remarks>
/// Sent by the client to the agent as a request. May create, continue or restart a task.
/// </remarks>
public class MessageSendParams
{
    /// <summary>
    /// The message being sent to the server.
    /// </summary>
    [JsonPropertyName("message")]
    [JsonRequired]
    public Message Message { get; set; } = new Message();

    /// <summary>
    /// Send message configuration.
    /// </summary>
    [JsonPropertyName("configuration")]
    public MessageSendConfiguration? Configuration { get; set; }

    /// <summary>
    /// Extension metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

/// <summary>
/// Configuration for the send message request.
/// </summary>
public class MessageSendConfiguration
{
    /// <summary>
    /// Accepted output modalities by the client.
    /// </summary>
    [JsonPropertyName("acceptedOutputModes")]
    public List<string>? AcceptedOutputModes { get; set; }

    /// <summary>
    /// Where the server should send notifications when disconnected.
    /// </summary>
    [JsonPropertyName("pushNotification")]
    public PushNotificationConfig? PushNotification { get; set; }

    /// <summary>
    /// Number of recent messages to be retrieved.
    /// </summary>
    [JsonPropertyName("historyLength")]
    public int? HistoryLength { get; set; }

    /// <summary>
    /// If the server should treat the client as a blocking request.
    /// </summary>
    [JsonPropertyName("blocking")]
    public bool Blocking { get; set; } = false;
}

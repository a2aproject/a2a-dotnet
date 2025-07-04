using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents parameters for sending a message to an agent.
/// </summary>
public class MessageSendParams
{
    /// <summary>
    /// Gets or sets the message to be sent to the agent.
    /// </summary>
    [JsonPropertyName("message")]
    [JsonRequired]
    public Message Message { get; set; } = new Message();

    /// <summary>
    /// Gets or sets optional configuration for message sending behavior.
    /// </summary>
    [JsonPropertyName("configuration")]
    public MessageSendConfiguration? Configuration { get; set; }

    /// <summary>
    /// Gets or sets additional metadata associated with the message send operation.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

/// <summary>
/// Represents configuration options for message sending behavior.
/// </summary>
public class MessageSendConfiguration
{
    /// <summary>
    /// Gets or sets the list of output modes that the client can accept from the agent.
    /// </summary>
    [JsonPropertyName("acceptedOutputModes")]
    public List<string>? AcceptedOutputModes { get; set; }

    /// <summary>
    /// Gets or sets push notification configuration for the message.
    /// </summary>
    [JsonPropertyName("pushNotification")]
    public PushNotificationConfig? PushNotification { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of history entries to include in the context.
    /// </summary>
    [JsonPropertyName("historyLength")]
    public int? HistoryLength { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the message send operation should block until completion.
    /// </summary>
    [JsonPropertyName("blocking")]
    public bool Blocking { get; set; } = false;
}

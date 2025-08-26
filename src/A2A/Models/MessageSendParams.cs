using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Parameters for sending a message request to an agent.
/// </summary>
/// <remarks>
/// Sent by the client to the agent as a request. May create, continue or restart a task.
/// </remarks>
public sealed class MessageSendParams
{
    /// <summary>
    /// The base-typed value of the message being sent to the server, required for JSON serialization to properly handle serializing the discriminator value.
    /// </summary>
    /// <remarks>We hide this from external devs as it's strictly a serialization nuance due to JSON polymorphism and discriminators.</remarks>
    [JsonInclude, JsonPropertyName("message"), JsonRequired, Obsolete("This property is only to be used during internal de/serialization", error: false)]
    internal A2AEvent JsonValue
    {
        get => this.Message;
        set
        {
            try
            {
                this.Message = (Message)value;
            }
            catch (Exception e)
            {
                throw new A2AException("Invalid MessageSendParameter", e, A2AErrorCode.InvalidRequest);
            }
        }
    }

    /// <summary>
    /// The message being sent to the server.
    /// </summary>
    [JsonIgnore]
    public Message Message { get; set; } = new();

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
public sealed class MessageSendConfiguration
{
    /// <summary>
    /// Accepted output modalities by the client.
    /// </summary>
    [JsonPropertyName("acceptedOutputModes")]
    [JsonRequired]
    public List<string> AcceptedOutputModes { get; set; } = [];

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

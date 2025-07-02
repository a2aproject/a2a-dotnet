using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

public class MessageSendParams
{

    [JsonPropertyName("message")]
    [JsonRequired]
    public Message Message { get; set; } = new Message();

    [JsonPropertyName("configuration")]
    public MessageSendConfiguration? Configuration { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

public class MessageSendConfiguration
{
    [JsonPropertyName("acceptedOutputModes")]
    public List<string>? AcceptedOutputModes { get; set; }

    [JsonPropertyName("pushNotification")]
    public PushNotificationConfig? PushNotification { get; set; }

    [JsonPropertyName("historyLength")]
    public int? HistoryLength { get; set; }

    [JsonPropertyName("blocking")]
    public bool Blocking { get; set; } = false;
}

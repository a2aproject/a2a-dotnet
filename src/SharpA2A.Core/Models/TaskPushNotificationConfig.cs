using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.Core;

public class TaskPushNotificationConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("pushNotificationConfig")]
    public PushNotificationConfig PushNotificationConfig { get; set; } = new PushNotificationConfig();
}



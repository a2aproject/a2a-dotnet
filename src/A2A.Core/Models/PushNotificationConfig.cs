using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.Core;

public class PushNotificationConfig
{
    [JsonPropertyName("url")]
    [JsonRequired]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("authentication")]
    public AuthenticationInfo? Authentication { get; set; }
}
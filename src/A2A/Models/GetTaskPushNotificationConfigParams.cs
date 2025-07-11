using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Parameters for fetching a pushNotificationConfiguration associated with a Task.
/// </summary>
public sealed class GetTaskPushNotificationConfigParams
{
    /// <summary>
    /// Task id.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Extension metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    /// <summary>
    /// Optional push notification configuration ID to retrieve a specific configuration.
    /// </summary>
    [JsonPropertyName("pushNotificationConfigId")]
    public string? PushNotificationConfigId { get; set; }
}

namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents the response to a list push notification config request.</summary>
public sealed class ListTaskPushNotificationConfigResponse
{
    /// <summary>Gets or sets the list of push notification configurations.</summary>
    [JsonPropertyName("configs")]
    public List<TaskPushNotificationConfig>? Configs { get; set; }

    /// <summary>Gets or sets the token for the next page of results.</summary>
    [JsonPropertyName("nextPageToken")]
    public string? NextPageToken { get; set; }
}

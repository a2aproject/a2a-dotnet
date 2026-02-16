namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Identifies which payload field is set on a SendMessageResponse.</summary>
public enum SendMessageResponseCase
{
    /// <summary>No payload field is set.</summary>
    None,
    /// <summary>Task payload.</summary>
    Task,
    /// <summary>Message payload.</summary>
    Message,
}

/// <summary>Represents the response to a send message request. Uses field-presence to indicate whether the result is a task or a message.</summary>
public sealed class SendMessageResponse
{
    /// <summary>Gets or sets the task result.</summary>
    [JsonPropertyName("task")]
    public AgentTask? Task { get; set; }

    /// <summary>Gets or sets the message result.</summary>
    [JsonPropertyName("message")]
    public Message? Message { get; set; }

    /// <summary>Gets which payload field is currently set.</summary>
    [JsonIgnore]
    public SendMessageResponseCase PayloadCase =>
        Task is not null ? SendMessageResponseCase.Task :
        Message is not null ? SendMessageResponseCase.Message :
        SendMessageResponseCase.None;
}

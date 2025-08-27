using System.Net.ServerSentEvents;
using System.Text.Json;

namespace A2A;

/// <summary>
/// Extension methods for the <see cref="A2AClient"/> class making its API more
/// convenient for certain use-cases.
/// </summary>
public static class A2AClientExtensions
{
    /// <inheritdoc cref="A2AClient.SendMessageAsync(MessageSendParams, CancellationToken)"/>
    public static Task<A2AResponse> SendMessageAsync(
        this A2AClient client,
        AgentMessage message,
        MessageSendConfiguration? configuration = null,
        Dictionary<string, JsonElement>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        return client.SendMessageAsync(
            new MessageSendParams
            {
                Message = message,
                Configuration = configuration,
                Metadata = metadata
            },
            cancellationToken);
    }

    /// <inheritdoc cref="A2AClient.CancelTaskAsync(TaskIdParams, CancellationToken)"/>
    public static Task<AgentTask> CancelTaskAsync(
        this A2AClient client,
        string taskId,
        Dictionary<string, JsonElement>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        return client.CancelTaskAsync(
            new TaskIdParams
            {
                Id = taskId,
                Metadata = metadata
            },
            cancellationToken);
    }

    /// <inheritdoc cref="A2AClient.SetPushNotificationAsync(TaskPushNotificationConfig, CancellationToken)"/>
    public static Task<TaskPushNotificationConfig> SetPushNotificationAsync(
        this A2AClient client,
        string taskId,
        string url,
        string? configId = null,
        string? token = null,
        PushNotificationAuthenticationInfo? authentication = null,
        CancellationToken cancellationToken = default)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        return client.SetPushNotificationAsync(
            new TaskPushNotificationConfig
            {
                TaskId = taskId,
                PushNotificationConfig = new PushNotificationConfig
                {
                    Id = configId,
                    Url = url,
                    Token = token,
                    Authentication = authentication
                }
            },
            cancellationToken);
    }

    /// <inheritdoc cref="A2AClient.GetPushNotificationAsync(GetTaskPushNotificationConfigParams, CancellationToken)"/>
    public static Task<TaskPushNotificationConfig> GetPushNotificationAsync(
        this A2AClient client,
        string taskId,
        string configId,
        Dictionary<string, JsonElement>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        return client.GetPushNotificationAsync(
            new GetTaskPushNotificationConfigParams
            {
                Id = taskId,
                PushNotificationConfigId = configId,
                Metadata = metadata
            },
            cancellationToken);
    }

    /// <inheritdoc cref="A2AClient.SendMessageStreamingAsync(MessageSendParams, CancellationToken)"/>
    public static IAsyncEnumerable<SseItem<A2AEvent>> SendMessageStreamingAsync(
        this A2AClient client,
        AgentMessage message,
        MessageSendConfiguration? configuration = null,
        Dictionary<string, JsonElement>? metadata = null,
        CancellationToken cancellationToken = default)

    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        return client.SendMessageStreamingAsync(
            new MessageSendParams
            {
                Message = message,
                Configuration = configuration,
                Metadata = metadata
            },
            cancellationToken);
    }
}

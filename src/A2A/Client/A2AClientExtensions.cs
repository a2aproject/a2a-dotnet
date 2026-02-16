namespace A2A;

/// <summary>Provides extension methods for <see cref="IA2AClient"/>.</summary>
public static class A2AClientExtensions
{
    /// <summary>Sends a text message to the agent and returns the response.</summary>
    /// <param name="client">The A2A client.</param>
    /// <param name="text">The text message to send.</param>
    /// <param name="role">The role of the message sender.</param>
    /// <param name="contextId">An optional context identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The send message response.</returns>
    public static Task<SendMessageResponse> SendMessageAsync(
        this IA2AClient client,
        string text,
        Role role = Role.User,
        string? contextId = null,
        CancellationToken cancellationToken = default)
    {
        var request = new SendMessageRequest
        {
            Message = new Message
            {
                Role = role,
                Parts = [Part.FromText(text)],
                ContextId = contextId,
                MessageId = Guid.NewGuid().ToString("N"),
            },
        };

        return client.SendMessageAsync(request, cancellationToken);
    }

    /// <summary>Sends a streaming text message to the agent.</summary>
    /// <param name="client">The A2A client.</param>
    /// <param name="text">The text message to send.</param>
    /// <param name="role">The role of the message sender.</param>
    /// <param name="contextId">An optional context identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An async enumerable of streaming response events.</returns>
    public static IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(
        this IA2AClient client,
        string text,
        Role role = Role.User,
        string? contextId = null,
        CancellationToken cancellationToken = default)
    {
        var request = new SendMessageRequest
        {
            Message = new Message
            {
                Role = role,
                Parts = [Part.FromText(text)],
                ContextId = contextId,
                MessageId = Guid.NewGuid().ToString("N"),
            },
        };

        return client.SendStreamingMessageAsync(request, cancellationToken);
    }
}

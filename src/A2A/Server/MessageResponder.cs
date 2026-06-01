using System.Text.Json;

namespace A2A;

/// <summary>
/// Convenience wrapper around <see cref="AgentEventQueue"/> for sending
/// agent response messages. Handles Role, MessageId, and ContextId automatically.
/// </summary>
/// <param name="eventQueue">The event queue to write messages to.</param>
/// <param name="contextId">The context ID to include on all messages.</param>
public sealed class MessageResponder(AgentEventQueue eventQueue, string contextId)
{
    /// <summary>Gets the context ID this responder operates on.</summary>
    public string ContextId => contextId;

    /// <summary>Send a text reply.</summary>
    /// <param name="text">The text content of the reply.</param>
    /// <param name="metadata">Optional metadata to attach to the message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask ReplyAsync(string text, Dictionary<string, JsonElement>? metadata = null, CancellationToken cancellationToken = default)
        => ReplyAsync([Part.FromText(text)], metadata, cancellationToken);

    /// <summary>Send a reply with the specified parts.</summary>
    /// <param name="parts">The content parts of the reply.</param>
    /// <param name="metadata">Optional metadata to attach to the message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask ReplyAsync(List<Part> parts, Dictionary<string, JsonElement>? metadata = null, CancellationToken cancellationToken = default)
        => eventQueue.EnqueueMessageAsync(new Message
        {
            Role = Role.Agent,
            MessageId = Guid.NewGuid().ToString("N"),
            ContextId = contextId,
            Parts = parts,
            Metadata = metadata,
        }, cancellationToken);
}
